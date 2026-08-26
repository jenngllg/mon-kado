using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL persistence operations for guest sessions.
/// </summary>
/// <param name="context">The database context.</param>
public class GuestSessionRepository(MonKadoDbContext context) : IGuestSessionRepository
{
    /// <inheritdoc />
    public void Add(GuestSession session)
    {
        context.GuestSessions.Add(session);
    }

    /// <inheritdoc />
    public Task<GuestSession?> GetByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        return context.GuestSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                session => session.Id == sessionId,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> DeleteExpiredAsync(
        DateTime expiresBefore,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var expiredIds = context.GuestSessions
            .Where(session => session.ExpiresAt <= expiresBefore)
            .OrderBy(session => session.ExpiresAt)
            .ThenBy(session => session.Id)
            .Select(session => session.Id)
            .Take(batchSize);

        return context.GuestSessions
            .Where(session => expiredIds.Contains(session.Id))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
