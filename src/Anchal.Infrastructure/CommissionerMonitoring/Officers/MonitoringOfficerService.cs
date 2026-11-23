using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Application.CommissionerMonitoring.DTOs;
using Anchal.Application.CommissionerMonitoring.Services;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Domain.CommissionerMonitoring.Rules;
using Anchal.Infrastructure.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Anchal.Infrastructure.CommissionerMonitoring.Officers;

public sealed class MonitoringOfficerService(
    CommissionerMonitoringDbContext db,
    MonitoringPasswordHasher hasher,
    IMonitoringService monitoring,
    TimeProvider clock) : IMonitoringOfficerService
{
    private const int MinimumPasswordLength = 10;

    public Task<MonitoringResult<IReadOnlyList<OfficerProfileDto>>> ListAsync(ClaimsPrincipal principal, CancellationToken ct = default) =>
        Run<IReadOnlyList<OfficerProfileDto>>(async () =>
        {
            await RoleManager(principal, ct);
            var officers = await Officers().OrderBy(x => x.DisplayName).ToListAsync(ct);
            return officers.Select(x => Profile(x)).ToArray();
        });

    public Task<MonitoringResult<CreatedOfficerDto>> CreateAsync(ClaimsPrincipal principal, CreateOfficerCommand command, CancellationToken ct = default) =>
        Run(async () =>
        {
            await RoleManager(principal, ct);
            var username = (command.Username ?? string.Empty).Trim();
            var name = (command.Name ?? string.Empty).Trim();
            var designation = (command.Designation ?? string.Empty).Trim();
            if (username.Length is 0 or > 120) throw Validation("username", "Enter a username");
            if (name.Length is 0 or > 160) throw Validation("name", "Enter the officer's name");
            if (designation.Length is 0 or > 120) throw Validation("designation", "Enter a designation");
            // The server decides who may be created. The client agreed to field officers and extra
            // Anchal logins; a Commissioner or RoleManager is a seeded fixture, and a UI that offers
            // two choices over an API that accepts four is a hole. Ordinal match: "commissioner" is
            // not a spelling of anything permitted either.
            var role = command.Role;
            if (role is not (MonitoringPolicies.FieldOfficerRole or MonitoringPolicies.OfficeRole))
                throw Validation("role", "Select a valid role");
            // Both permitted roles bind to exactly one of the six existing Anchals. No Anchal is made here.
            if (command.HomeOfficeId is not (>= 1 and <= 6)) throw Validation("homeOfficeId", "Select an Anchal");
            var homeOffice = command.HomeOfficeId.Value;

            var office = await db.Offices.SingleOrDefaultAsync(
                x => x.Code == "anchal" + homeOffice.ToString(CultureInfo.InvariantCulture) && x.IsActive, ct)
                ?? throw Validation("homeOfficeId", "Select an Anchal");

            var normalized = username.ToUpperInvariant();
            if (await db.Users.AnyAsync(x => x.NormalizedLoginName == normalized, ct))
                throw new MonitoringRuleException(409, new("CONFLICT", "That username is already taken"));

            var password = GeneratePassword();
            var officer = new MonitoringUser
            {
                Id = Guid.NewGuid(),
                LoginName = username,
                NormalizedLoginName = normalized,
                PasswordHash = hasher.HashPassword(password),
                DisplayName = name,
                Designation = designation,
                Role = role,
                MustChangePassword = true,
                IsActive = true,
                CreatedAt = clock.GetUtcNow()
            };
            db.Users.Add(officer);
            db.UserOffices.Add(new MonitoringUserOffice
            {
                UserId = officer.Id,
                OfficeId = office.Id,
                AssignedAt = clock.GetUtcNow()
            });
            await db.SaveChangesAsync(ct);
            return new CreatedOfficerDto(Profile(officer, homeOffice), password);
        });

    public Task<MonitoringResult<OfficerProfileDto>> SetActiveAsync(ClaimsPrincipal principal, Guid officerId, bool active, CancellationToken ct = default) =>
        Run(async () =>
        {
            await RoleManager(principal, ct);
            var officer = await Load(officerId, ct);
            officer.IsActive = active;
            await db.SaveChangesAsync(ct);
            return Profile(officer);
        });

    public Task<MonitoringResult<TemporaryPasswordDto>> ResetPasswordAsync(ClaimsPrincipal principal, Guid officerId, CancellationToken ct = default) =>
        Run(async () =>
        {
            await RoleManager(principal, ct);
            var officer = await Load(officerId, ct);
            var password = GeneratePassword();
            officer.PasswordHash = hasher.HashPassword(password);
            officer.MustChangePassword = true;
            await db.SaveChangesAsync(ct);
            return new TemporaryPasswordDto(password);
        });

    public Task<MonitoringResult<bool>> ChangeOwnPasswordAsync(ClaimsPrincipal principal, ChangePasswordCommand command, CancellationToken ct = default) =>
        Run(async () =>
        {
            var session = await monitoring.GetSessionAsync(principal, ct);
            if (!session.IsSuccess) throw new MonitoringRuleException(session.Status, session.Error!);
            if (session.Value!.Data.Role is not ("officer" or "office"))
                throw new MonitoringRuleException(403, new("FORBIDDEN", "officer access required"));

            var officer = await db.Users.SingleOrDefaultAsync(x => x.Id == session.Value.Data.UserId, ct)
                ?? throw new MonitoringRuleException(403, new("FORBIDDEN", "Monitoring account unavailable"));
            if (string.IsNullOrEmpty(command.CurrentPassword) ||
                !hasher.VerifyPassword(command.CurrentPassword, officer.PasswordHash))
                throw Validation("currentPassword", "Current password is incorrect");
            if ((command.NewPassword ?? string.Empty).Length < MinimumPasswordLength)
                throw Validation("newPassword", $"Use at least {MinimumPasswordLength} characters");
            if (hasher.VerifyPassword(command.NewPassword!, officer.PasswordHash))
                throw Validation("newPassword", "Choose a password you have not used here before");

            officer.PasswordHash = hasher.HashPassword(command.NewPassword!);
            officer.MustChangePassword = false;
            await db.SaveChangesAsync(ct);
            return true;
        });

    private IQueryable<MonitoringUser> Officers() => db.Users
        .Include(x => x.OfficeAssignments).ThenInclude(x => x.Office)
        // The role manager administers the people it may create. The commissioner and the role
        // manager itself are not in this set: it can neither lock them out nor reset their passwords.
        .Where(x => x.Role == MonitoringPolicies.FieldOfficerRole || x.Role == MonitoringPolicies.OfficeRole);

    private async Task<MonitoringUser> Load(Guid officerId, CancellationToken ct) =>
        await Officers().SingleOrDefaultAsync(x => x.Id == officerId, ct)
        ?? throw new MonitoringRuleException(404, new("NOT_FOUND", "Officer not found"));

    private async Task RoleManager(ClaimsPrincipal principal, CancellationToken ct)
    {
        var session = await monitoring.GetSessionAsync(principal, ct);
        if (!session.IsSuccess) throw new MonitoringRuleException(session.Status, session.Error!);
        if (session.Value!.Data.Role != "rolemanager")
            throw new MonitoringRuleException(403, new("FORBIDDEN", "role manager access required"));
    }

    private static OfficerProfileDto Profile(MonitoringUser officer, int? homeOfficeId = null) => new(
        officer.Id, officer.LoginName, officer.DisplayName, officer.Designation ?? string.Empty,
        homeOfficeId ?? OfficeNumber(officer), officer.IsActive, officer.Role);

    private static int OfficeNumber(MonitoringUser officer)
    {
        var code = officer.OfficeAssignments.SingleOrDefault()?.Office?.Code;
        return code is { Length: > 6 } && int.TryParse(code[6..], CultureInfo.InvariantCulture, out var number)
            ? number : 0;
    }

    // A readable one-time password: the officer types it once, from a slip.
    private static string GeneratePassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var characters = new char[14];
        for (var index = 0; index < characters.Length; index++)
            characters[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(characters);
    }

    private static MonitoringRuleException Validation(string field, string message) =>
        new(422, new("VALIDATION", "Please correct the highlighted fields",
            new Dictionary<string, string> { [field] = message }));

    private static async Task<MonitoringResult<T>> Run<T>(Func<Task<T>> action)
    {
        try { return MonitoringResult<T>.Success(await action()); }
        catch (MonitoringRuleException exception) { return MonitoringResult<T>.Failure(exception); }
    }
}
