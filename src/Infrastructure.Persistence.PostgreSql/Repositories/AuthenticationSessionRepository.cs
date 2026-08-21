using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

internal class AuthenticationSessionRepository(MonKadoDbContext context)
    : IAuthenticationSessionRepository
{
    public void Add(AuthenticationSession session)
    {
        context.AuthenticationSessions.Add(session);
    }

    public Task<AuthenticationSession?> GetByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {

        return context.AuthenticationSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                session => session.Id == sessionId,
                cancellationToken);
    }

    public async Task UpdateAsync(
        Guid sessionId,
        Guid userId,
        byte[] protectedTicket,
        DateTime renewedAt,
        DateTime expiresAt,
        CancellationToken cancellationToken)
    {
        await context.AuthenticationSessions
            .Where(session => session.Id == sessionId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        session => session.UserId,
                        userId)
                    .SetProperty(
                        session => session.ProtectedTicket,
                        protectedTicket)
                    .SetProperty(
                        session => session.RenewedAt,
                        renewedAt)
                    .SetProperty(
                        session => session.ExpiresAt,
                        expiresAt),
                cancellationToken);
    }

    public async Task DeleteAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        await context.AuthenticationSessions
            .Where(session => session.Id == sessionId)
            .ExecuteDeleteAsync(cancellationToken);
    }

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
