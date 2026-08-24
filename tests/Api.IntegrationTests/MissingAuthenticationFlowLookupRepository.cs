using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class MissingAuthenticationFlowLookupRepository(
    MonKadoDbContext context) : IAuthenticationSessionRepository
{
    private readonly AuthenticationSessionRepository _repository = new(context);

    public void Add(AuthenticationSession session)
    {
        _repository.Add(session);
    }

    public Task<AuthenticationSession?> GetByIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {
        _ = sessionId;
        _ = cancellationToken;

        return Task.FromResult<AuthenticationSession?>(null);
    }

    public Task<AuthenticationSession?> GetByIdForUpdateAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {

        return _repository.GetByIdForUpdateAsync(
            sessionId,
            cancellationToken);
    }

    public Task<Guid?> GetUserIdAsync(
        Guid sessionId,
        CancellationToken cancellationToken)
    {

        return _repository.GetUserIdAsync(
            sessionId,
            cancellationToken);
    }

    public Task<int> DeleteExpiredAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {

        return _repository.DeleteExpiredAsync(
            cutoff,
            batchSize,
            cancellationToken);
    }

    public Task<int> RevokeAllForUserAsync(
        Guid userId,
        DateTime revokedAt,
        CancellationToken cancellationToken)
    {

        return _repository.RevokeAllForUserAsync(
            userId,
            revokedAt,
            cancellationToken);
    }
}
