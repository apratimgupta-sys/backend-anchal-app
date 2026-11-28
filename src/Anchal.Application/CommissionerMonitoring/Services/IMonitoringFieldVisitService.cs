using System.Security.Claims;
using Anchal.Application.CommissionerMonitoring.DTOs;

namespace Anchal.Application.CommissionerMonitoring.Services;

public interface IMonitoringFieldVisitService
{
    Task<MonitoringResult<FieldVisitDto>> CreateAsync(ClaimsPrincipal principal, SaveFieldVisitCommand command, CancellationToken ct = default);
    Task<MonitoringResult<FieldVisitPageDto>> ListAsync(ClaimsPrincipal principal, FieldVisitFilter filter, CancellationToken ct = default);
    Task<MonitoringResult<FieldVisitSummaryDto>> SummaryAsync(ClaimsPrincipal principal, VisitSummaryFilter filter, CancellationToken ct = default);
    Task<MonitoringResult<FieldVisitDto>> VoidAsync(ClaimsPrincipal principal, Guid visitId, string reason, CancellationToken ct = default);
}
