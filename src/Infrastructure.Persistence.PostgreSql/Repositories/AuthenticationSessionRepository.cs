using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL persistence operations for authentication sessions.
/// </summary>
/// <param name="context">The database context.</param>
public class AuthenticationSessionRepository(MonKadoDbContext context)
    : IAuthenticationSessionRepository
{
    /// <inheritdoc />
    public void Add(AuthenticationSession session)
    {
        context.AuthenticationSessions.Add(session);
    }

    /// <inheritdoc />
    public Task<AuthenticationSession?> GetByIdForUpdateAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return context.AuthenticationSessions
            .FromSqlInterpolated(
                $"SELECT * FROM public.authentication_sessions WHERE id = {sessionId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> DeleteExpiredAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var expiredSessionIds = await context.AuthenticationSessions
            .AsNoTracking()
            .Where(session => session.ExpiresAt <= cutoff)
            .OrderBy(session => session.ExpiresAt)
            .ThenBy(session => session.Id)
            .Select(session => session.Id)
            .Take(batchSize)
            .ToArrayAsync(cancellationToken);

        return expiredSessionIds.Length == 0
            ? 0
            : await context.AuthenticationSessions
                .Where(session =>
                    expiredSessionIds.Contains(session.Id) &&
                    session.ExpiresAt <= cutoff)
                .ExecuteDeleteAsync(cancellationToken);
    }
}
