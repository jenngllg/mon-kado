using JennGllg.Fr.MonKado.Back.Application.Accounts;
using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

internal sealed class ExpiredAuthenticationSessionCleanup(MonKadoDbContext context)
    : IExpiredAuthenticationSessionCleanup
{
    public async Task<int> DeleteExpiredSessionsAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        Guid[] expiredSessionIds = await context.AuthenticationSessions
            .AsNoTracking()
            .Where(session => session.ExpiresAt <= cutoff)
            .OrderBy(session => session.ExpiresAt)
            .ThenBy(session => session.Id)
            .Select(session => session.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        if (expiredSessionIds.Length == 0)
        {
            return 0;
        }

        return await context.AuthenticationSessions
            .Where(session =>
                expiredSessionIds.Contains(session.Id) &&
                session.ExpiresAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
