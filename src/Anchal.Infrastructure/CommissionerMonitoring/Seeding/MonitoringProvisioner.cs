using Microsoft.EntityFrameworkCore;
using Anchal.Application.CommissionerMonitoring.Auth;
using Anchal.Domain.CommissionerMonitoring;
using Anchal.Infrastructure.CommissionerMonitoring.Auth;
using Anchal.Infrastructure.CommissionerMonitoring.Persistence;

namespace Anchal.Infrastructure.CommissionerMonitoring.Seeding;

// Invoked explicitly after the monitoring migration with operator-supplied credentials.
// It is never part of ordinary API startup or the existing Apna Ranchi seeder.
public sealed class MonitoringProvisioner(CommissionerMonitoringDbContext db, MonitoringPasswordHasher hasher)
    : IMonitoringProvisioner
{
    private static readonly string[] LoginNames =
        ["anchal1", "anchal2", "anchal3", "anchal4", "anchal5", "anchal6", "commissioner"];

    // Copied from the approved Cloudflare monitoring service catalog, keyed by its stable service ID.
    private static readonly string[] ServiceNames =
    [
        "नए होल्डिंग प्राप्त करने हेतु आवेदन", "Holding Tax का भुगतान",
        "Municipal Trade Licence के लिए आवेदन", "New जल संयोजन के लिए आवेदन",
        "Water User Charge का भुगतान", "Waste User Charge का भुगतान",
        "होल्डिंग में त्रुटि सुधार", "घर पर कचरा वाहन की सेवा से संबंधित",
        "Fogging से संबंधित", "Street Dog से संबंधित", "जन्म एवं मृत्यु निबंधन",
        "जन शिकायत", "पथ बत्ती", "सड़क एवं नाली निर्माण से संबंधित",
        "Grass Cutting", "Tree Pruning", "Water Bodies के सफाई से संबंधित",
        "प्रधान मंत्री आवास योजना", "SSG Group", "Vendor Licence",
        "PM SVANidhi Licence", "Revolving Fund", "Self Employment Programme",
        "Lodge/Hostel/Banquet Hall Licence", "Community Hall Booking"
    ];

    public async Task ProvisionAsync(
        IReadOnlyDictionary<string, string> credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        if (credentials.Count != LoginNames.Length ||
            LoginNames.Any(name => !credentials.TryGetValue(name, out var password) ||
                password.Length < 12 || password.Length > 128))
        {
            throw new ArgumentException(
                "Provide a 12-128 character password for each of the seven approved monitoring accounts.",
                nameof(credentials));
        }

        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken) : null;
        var offices = await db.Offices.ToDictionaryAsync(x => x.Code, cancellationToken);
        var users = await db.Users.Include(x => x.OfficeAssignments)
            .ToDictionaryAsync(x => x.NormalizedLoginName, cancellationToken);
        var catalog = await db.CatalogItems.Where(x => x.Kind == MonitoringActivityKind.Service)
            .ToDictionaryAsync(x => x.Key, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        for (var i = 1; i <= 6; i++)
        {
            var code = $"anchal{i}";
            if (!offices.TryGetValue(code, out var office))
            {
                office = new MonitoringOffice
                {
                    Id = Guid.NewGuid(), Code = code, Name = $"Anchal {i}", IsActive = true
                };
                db.Offices.Add(office);
            }
            await ProvisionUser(code, MonitoringPolicies.OfficeRole, office, users, credentials, now);
        }

        await ProvisionUser("commissioner", MonitoringPolicies.CommissionerRole, null,
            users, credentials, now);

        for (var i = 0; i < ServiceNames.Length; i++)
        {
            var key = (i + 1).ToString();
            if (catalog.TryGetValue(key, out var item))
            {
                item.Name = ServiceNames[i];
                item.SortOrder = i + 1;
            }
            else
            {
                db.CatalogItems.Add(new MonitoringCatalogItem
                {
                    Id = Guid.NewGuid(), Kind = MonitoringActivityKind.Service, Key = key,
                    Name = ServiceNames[i], SortOrder = i + 1, IsActive = true
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken);
    }

    private Task ProvisionUser(
        string loginName, string role, MonitoringOffice? office,
        IReadOnlyDictionary<string, MonitoringUser> users,
        IReadOnlyDictionary<string, string> credentials, DateTimeOffset now)
    {
        var normalized = loginName.ToUpperInvariant();
        if (users.TryGetValue(normalized, out var existing))
        {
            if (existing.Role != role ||
                (office is null && existing.OfficeAssignments.Count != 0) ||
                (office is not null && existing.OfficeAssignments.Any(x => x.OfficeId != office.Id)))
            {
                throw new InvalidOperationException($"Monitoring account {loginName} has a conflicting role or office assignment.");
            }
            if (office is not null && existing.OfficeAssignments.Count == 0)
            {
                db.UserOffices.Add(new MonitoringUserOffice
                {
                    UserId = existing.Id, OfficeId = office.Id, AssignedAt = now
                });
            }
            return Task.CompletedTask;
        }

        var user = new MonitoringUser
        {
            Id = Guid.NewGuid(), LoginName = loginName, NormalizedLoginName = normalized,
            DisplayName = office?.Name ?? "Commissioner", Role = role,
            PasswordHash = hasher.HashPassword(credentials[loginName]),
            IsActive = true, CreatedAt = now
        };
        db.Users.Add(user);
        if (office is not null)
        {
            db.UserOffices.Add(new MonitoringUserOffice
            {
                User = user, Office = office, AssignedAt = now
            });
        }
        return Task.CompletedTask;
    }
}
