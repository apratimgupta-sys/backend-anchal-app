using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Anchal.Infrastructure.CommissionerMonitoring.Persistence;

public sealed partial class CommissionerMonitoringDbContext
{
    public async Task<T> MutateOfficeAsync<T>(Guid officeId, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await using var transaction = Database.IsRelational()
            ? await Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken) : null;
        try
        {
            if (transaction is not null)
            {
                // Serialize an office's complete ledger, including first setup and request replays.
                // ReadCommitted ensures reads after waiting see the preceding writer's commit.
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT 1 FROM commissioner_monitoring.monitoring_offices WHERE \"Id\" = {officeId} FOR UPDATE",
                    cancellationToken);
            }
            var result = await operation();
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            if (transaction is not null) await transaction.RollbackAsync(CancellationToken.None);
            ChangeTracker.Clear();
            throw;
        }
    }
}
