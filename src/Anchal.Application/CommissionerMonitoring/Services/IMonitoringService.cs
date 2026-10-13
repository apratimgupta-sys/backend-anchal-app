using System.Security.Claims;
using Anchal.Application.CommissionerMonitoring.DTOs;

namespace Anchal.Application.CommissionerMonitoring.Services;

public interface IMonitoringService
{
    Task<MonitoringResult<MonitoringRead<SessionDto>>> GetSessionAsync(ClaimsPrincipal principal, CancellationToken ct = default);
    Task<MonitoringResult<MonitoringRead<IReadOnlyList<ServiceDto>>>> GetServicesAsync(ClaimsPrincipal principal, CancellationToken ct = default);
    Task<MonitoringResult<MonitoringRead<SetupDto?>>> GetSetupAsync(ClaimsPrincipal principal, CancellationToken ct = default);
    Task<MonitoringResult<SetupDto>> SaveSetupAsync(ClaimsPrincipal principal, SetupCommand command, CancellationToken ct = default);
    Task<MonitoringResult<MonitoringRead<EntriesDto>>> GetEntriesAsync(ClaimsPrincipal principal, string mode, DateOnly date, CancellationToken ct = default);
    Task<MonitoringResult<EntryDto>> SaveEntryAsync(ClaimsPrincipal principal, SaveEntryCommand command, Guid? id = null, CancellationToken ct = default);
    Task<MonitoringResult<MonitoringRead<DashboardDto>>> GetDashboardAsync(ClaimsPrincipal principal, PeriodFilter filter, CancellationToken ct = default);
    Task<MonitoringResult<MonitoringRead<IReadOnlyList<EntryDto>>>> GetDashboardEntriesAsync(ClaimsPrincipal principal, PeriodFilter filter, CancellationToken ct = default);
    Task<MonitoringResult<MonitoringRead<IReadOnlyList<AuditDto>>>> GetHistoryAsync(ClaimsPrincipal principal, CancellationToken ct = default);
}
