using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL persistence operations for MonKado users.
/// </summary>
/// <param name="context">The database context.</param>
public class MonKadoUserRepository(MonKadoDbContext context) : IMonKadoUserRepository
{
    /// <inheritdoc />
    public IQueryable<MonKadoUser> Query()
    {

        return context.Users.AsNoTracking();
    }

    /// <inheritdoc />
    public Task<MonKadoUser?> GetByIdForUpdateAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {

        return context.Users
            .FromSqlInterpolated(
                $"SELECT *, xmin FROM public.users WHERE id = {userId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<MonKadoUser?> GetByIdForUpdateAsync(
        Guid userId,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {

        return context.Users
            .FromSqlInterpolated($"""
                SELECT *, xmin FROM public.users
                WHERE id = {userId} AND normalized_email = {normalizedEmail}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<MonKadoUser?> GetByNormalizedEmailForUpdateAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {

        return context.Users
            .FromSqlInterpolated(
                $"SELECT *, xmin FROM public.users WHERE normalized_email = {normalizedEmail} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> DeleteExpiredUnconfirmedAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var expiredUserIds = await Query()
            .Where(user =>
                !user.EmailConfirmed &&
                user.UnconfirmedAccountExpiresAt != null &&
                user.UnconfirmedAccountExpiresAt <= cutoff)
            .OrderBy(user => user.UnconfirmedAccountExpiresAt)
            .ThenBy(user => user.Id)
            .Select(user => user.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        return expiredUserIds.Length == 0
            ? 0
            : await context.Users
                .Where(user =>
                    expiredUserIds.Contains(user.Id) &&
                    !user.EmailConfirmed &&
                    user.UnconfirmedAccountExpiresAt != null &&
                    user.UnconfirmedAccountExpiresAt <= cutoff)
                .ExecuteDeleteAsync(cancellationToken);
    }
}
