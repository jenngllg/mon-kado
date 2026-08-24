using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class MissingLockedUserRepository : IMonKadoUserRepository
{
    public void Add(MonKadoUser user)
    {

        throw new NotSupportedException();
    }

    public IQueryable<MonKadoUser> Query()
    {

        throw new NotSupportedException();
    }

    public Task<MonKadoUser?> GetByIdForUpdateAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {

        throw new NotSupportedException();
    }

    public Task<MonKadoUser?> GetByIdForUpdateAsync(
        Guid userId,
        string normalizedEmail,
        CancellationToken cancellationToken)
    {

        return Task.FromResult<MonKadoUser?>(null);
    }

    public Task<MonKadoUser?> GetByNormalizedEmailForUpdateAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {

        throw new NotSupportedException();
    }

    public Task<int> DeleteExpiredUnconfirmedAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {

        throw new NotSupportedException();
    }
}
