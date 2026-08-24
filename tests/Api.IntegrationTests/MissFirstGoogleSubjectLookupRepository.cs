using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class MissFirstGoogleSubjectLookupRepository(MonKadoDbContext context)
    : IGoogleAccountRepository
{
    private readonly GoogleAccountRepository _repository = new(context);
    private int _subjectLookupCount;

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

        if (Interlocked.Increment(ref _subjectLookupCount) == 1)
            return Task.FromResult<Guid?>(null);

        return _repository.GetMemberIdBySubjectAsync(
            subject,
            cancellationToken);
    }

    public Task<string?> GetSubjectByMemberIdAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {

        return _repository.GetSubjectByMemberIdAsync(
            memberId,
            cancellationToken);
    }
}
