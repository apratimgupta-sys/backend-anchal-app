using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Domain.CommissionerMonitoring;
using static Anchal.Domain.CommissionerMonitoring.Rules.MonitoringRules;

namespace Anchal.Application.CommissionerMonitoring.Services;

/// The dashboard's finer breakdowns: the same entries GetDashboardAsync rolls up per Anchal, rolled up per
/// service and per day instead. Every figure here must agree with that roll-up for the same filter;
/// DashboardBreakdownTests holds the two against each other.
public sealed partial class MonitoringService
{
    /// A per-day series longer than this is refused: a chart of more days than this cannot be read.
    public const int DayRowsMaximum = 92;

    public Task<MonitoringResult<MonitoringRead<DashboardServicesDto>>> GetDashboardServicesAsync(ClaimsPrincipal principal,
        PeriodFilter filter, CancellationToken ct = default) => Run(async () =>
    {
        await Authorize(principal, ct, "commissioner");
        await ValidateFilter(filter, ct);
        var (audits, offices) = await LoadOfficeEntries(filter, ct);
        // One row per service the daily entry collects, in id order. The sums are raw: received on a revenue
        // service is its number of payments, and the page decides what each shape shows.
        var rows = Enumerable.Range(1, ServiceCount).Where(DailyEntryCollects).Select(serviceId =>
        {
            long received = 0, completed = 0, pending = 0, amount = 0;
            foreach (var (setup, entries) in offices)
            {
                var period = entries.Where(x => x.Input.ServiceId == serviceId &&
                    x.Input.ServiceDate >= filter.From && x.Input.ServiceDate <= filter.To).ToArray();
                received += period.Sum(x => x.Input.Received);
                completed += period.Sum(x => x.Input.Completed);
                amount += period.Sum(x => x.Input.AmountPaise);
                if (MovesPendingBalance(serviceId)) pending += ClosingPending(setup, entries, serviceId, filter.To);
            }
            double? disposalPct = !MovesPendingBalance(serviceId) || received == 0 ? null : (double)completed / received * 100;
            return new ServiceRowDto(serviceId, received, completed, pending, disposalPct, amount);
        }).ToArray();
        return Read(new DashboardServicesDto(rows), LastUpdated(audits));
    });

    public Task<MonitoringResult<MonitoringRead<DashboardDaysDto>>> GetDashboardDaysAsync(ClaimsPrincipal principal,
        PeriodFilter filter, CancellationToken ct = default) => Run(async () =>
    {
        await Authorize(principal, ct, "commissioner");
        await ValidateFilter(filter, ct);
        var days = filter.To.DayNumber - filter.From.DayNumber + 1;
        if (days > DayRowsMaximum) Fail(422, $"Choose a period of {DayRowsMaximum} days or fewer");
        var (audits, offices) = await LoadOfficeEntries(filter, ct);
        // The same rule GetDashboardAsync applies: unpinned, only what the daily entry collects; pinned, that
        // service's own history. Received and disposed come from the services that keep a queue.
        bool CountsInFigures(int serviceId) => filter.ServiceId.HasValue || DailyEntryCollects(serviceId);
        var byDate = offices.SelectMany(x => Filter(x.Entries, filter)).Where(x => CountsInFigures(x.Input.ServiceId))
            .ToLookup(x => x.Input.ServiceDate);
        var rows = Enumerable.Range(0, days).Select(offset =>
        {
            var date = filter.From.AddDays(offset);
            var queue = byDate[date].Where(x => MovesPendingBalance(x.Input.ServiceId)).ToArray();
            return new DayRowDto(date, queue.Sum(x => x.Input.Received), queue.Sum(x => x.Input.Completed),
                byDate[date].Sum(x => x.Input.AmountPaise));
        }).ToArray();
        return Read(new DashboardDaysDto(rows), LastUpdated(audits));
    });

    /// Every configured office the filter allows, with its entries carrying their derived balances. An office
    /// with no initial balances has no entries and no balance, so it is left out.
    private async Task<(List<MonitoringAuditEvent> Audits, List<(SetupDto Setup, IReadOnlyList<EntryDto> Entries)> Offices)>
        LoadOfficeEntries(PeriodFilter filter, CancellationToken ct)
    {
        var audits = await LoadAudits(filter.OfficeId, ct);
        var all = CurrentEntries(audits);
        var offices = new List<(SetupDto, IReadOnlyList<EntryDto>)>();
        foreach (var office in await ActiveOffices(filter).OrderBy(x => x.Code).ToListAsync(ct))
        {
            var setup = ToDto(await LoadSetup(office.Id, ct));
            if (setup is null) continue;
            offices.Add((setup, Views(setup, all.Where(x => x.OfficeId == office.Id).ToArray())));
        }
        return (audits, offices);
    }

    /// The pending balance of one service at the end of one day: the closing balance of its last entry on or
    /// before that day, or its opening balance when it has none. The same expression GetDashboardAsync uses.
    private static long ClosingPending(SetupDto setup, IReadOnlyList<EntryDto> entries, int serviceId, DateOnly on)
    {
        var balance = setup.Balances.FirstOrDefault(x => x.ServiceId == serviceId);
        if (balance is null || on < setup.StartDate) return 0;
        return (entries.Where(x => x.Input.ServiceId == serviceId && x.Input.ServiceDate <= on)
            .OrderBy(x => x.Input.ServiceDate).LastOrDefault()?.Closing ?? balance.Opening).Service;
    }
}
