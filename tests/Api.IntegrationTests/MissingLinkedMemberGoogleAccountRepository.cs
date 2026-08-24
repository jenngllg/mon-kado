using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>
/// Simulates a Google subject whose linked member disappeared before the member lock.
/// </summary>
/// <param name="context">The database context.</param>
public class MissingLinkedMemberGoogleAccountRepository(MonKadoDbContext context)
    : IGoogleAccountRepository
{
    private readonly GoogleAccountRepository _repository = new(context);

    /// <summary>
    /// Gets the missing member identifier returned by the simulated subject lookup.
    /// </summary>
    public static Guid MissingMemberId
    {
        get;
    } = Guid.Parse(
        "01941f29-7c00-7000-8000-000000000001");

    /// <inheritdoc />
    public void AddLogin(
        Guid memberId,
        string subject)
    {
        _repository.AddLogin(
            memberId,
            subject);
    }

    /// <inheritdoc />
    public Task<Guid?> GetMemberIdBySubjectAsync(
        string subject,
        CancellationToken cancellationToken)
    {
        _ = subject;
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<Guid?>(MissingMemberId);
    }

    /// <inheritdoc />
    public Task<string?> GetSubjectByMemberIdAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {

        return _repository.GetSubjectByMemberIdAsync(
            memberId,
            cancellationToken);
    }
}
