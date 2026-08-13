using JennGllg.Fr.MonKado.Back.Application.Accounts;
using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

internal sealed class ExpiredAccountCleanup(MonKadoDbContext context) : IExpiredAccountCleanup
{
    public async Task<int> DeleteExpiredUnconfirmedAccountsAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        Guid[] expiredUserIds = await context.Users
            .AsNoTracking()
            .Where(user =>
                !user.EmailConfirmed &&
                user.UnconfirmedAccountExpiresAt != null &&
                user.UnconfirmedAccountExpiresAt <= cutoff)
            .OrderBy(user => user.UnconfirmedAccountExpiresAt)
            .ThenBy(user => user.Id)
            .Select(user => user.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        if (expiredUserIds.Length == 0)
        {
            return 0;
        }

        return await context.Users
            .Where(user =>
                expiredUserIds.Contains(user.Id) &&
                !user.EmailConfirmed &&
                user.UnconfirmedAccountExpiresAt != null &&
                user.UnconfirmedAccountExpiresAt <= cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
