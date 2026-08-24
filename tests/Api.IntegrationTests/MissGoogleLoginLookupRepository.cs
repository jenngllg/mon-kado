using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class MissGoogleLoginLookupRepository(MonKadoDbContext context)
    : IGoogleAccountRepository
{
    private readonly GoogleAccountRepository _repository = new(context);

    public void AddLogin(
        Guid memberId,
        string subject)
    {
        _repository.AddLogin(
            memberId,
            subject);
    }

    public Task<Guid?> GetMemberIdBySubjectAsync(
        string subject,
        CancellationToken cancellationToken)
    {
        _ = subject;
        _ = cancellationToken;

        return Task.FromResult<Guid?>(null);
    }

    public Task<string?> GetSubjectByMemberIdAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        _ = memberId;
        _ = cancellationToken;

        return Task.FromResult<string?>(null);
    }
}
