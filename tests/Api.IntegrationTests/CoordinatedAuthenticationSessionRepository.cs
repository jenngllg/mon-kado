using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class CoordinatedAuthenticationSessionRepository(
    AuthenticationSessionRepository repository,
    SessionLockCoordinator coordinator) : IAuthenticationSessionRepository
{
    public void Add(AuthenticationSession session)
    {
        repository.Add(session);
    }

    public async Task<AuthenticationSession?> GetByIdForUpdateAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var session = await repository.GetByIdForUpdateAsync(
            sessionId,
            cancellationToken);
        await coordinator.WaitAfterLockAsync(cancellationToken);

        return session;
    }

    public async Task<Guid?> GetUserIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        var userId = await repository.GetUserIdAsync(
            sessionId,
            cancellationToken);
        await coordinator.WaitAfterLookupAsync(cancellationToken);

        return userId;
    }

    public Task<int> DeleteExpiredAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        return repository.DeleteExpiredAsync(
            cutoff,
            batchSize,
            cancellationToken);
    }

    public Task<int> RevokeAllForUserAsync(
        Guid userId,
        DateTime revokedAt,
        CancellationToken cancellationToken)
    {
        return repository.RevokeAllForUserAsync(
            userId,
            revokedAt,
            cancellationToken);
    }
}
