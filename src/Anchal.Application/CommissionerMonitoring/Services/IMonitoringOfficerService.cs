using System.Security.Claims;
using Anchal.Application.CommissionerMonitoring.DTOs;

namespace Anchal.Application.CommissionerMonitoring.Services;

public interface IMonitoringOfficerService
{
    Task<MonitoringResult<IReadOnlyList<OfficerProfileDto>>> ListAsync(ClaimsPrincipal principal, CancellationToken ct = default);
    Task<MonitoringResult<CreatedOfficerDto>> CreateAsync(ClaimsPrincipal principal, CreateOfficerCommand command, CancellationToken ct = default);
    Task<MonitoringResult<OfficerProfileDto>> SetActiveAsync(ClaimsPrincipal principal, Guid officerId, bool active, CancellationToken ct = default);
    Task<MonitoringResult<TemporaryPasswordDto>> ResetPasswordAsync(ClaimsPrincipal principal, Guid officerId, CancellationToken ct = default);
    Task<MonitoringResult<bool>> ChangeOwnPasswordAsync(ClaimsPrincipal principal, ChangePasswordCommand command, CancellationToken ct = default);
}
