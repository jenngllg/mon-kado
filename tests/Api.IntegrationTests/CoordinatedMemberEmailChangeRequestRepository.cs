using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>
/// Suspends an untracked request read so an integration test can coordinate a concurrent account claim.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="coordinator">The request-read coordinator.</param>
public class CoordinatedMemberEmailChangeRequestRepository(
    MonKadoDbContext context,
    EmailChangeRequestReadCoordinator coordinator)
    : IMemberEmailChangeRequestRepository
{
    private readonly MemberEmailChangeRequestRepository _inner = new(context);

    /// <inheritdoc />
    public void Add(MemberEmailChangeRequest request)
    {
        _inner.Add(request);
    }

    /// <inheritdoc />
    public Task<MemberEmailChangeRequest?> GetActiveByUserIdForUpdateAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {

        return _inner.GetActiveByUserIdForUpdateAsync(
            userId,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<MemberEmailChangeRequest?> GetByIdForUpdateAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {

        return _inner.GetByIdForUpdateAsync(
            requestId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MemberEmailChangeRequest?> GetByIdAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var request = await _inner.GetByIdAsync(
            requestId,
            cancellationToken);
        coordinator.SignalRequestRead();
        await coordinator.WaitUntilReleasedAsync(cancellationToken);

        return request;
    }

    /// <inheritdoc />
    public Task<int> DeleteExpiredOrCompletedAsync(
        DateTime expirationCutoff,
        DateTime completedCutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {

        return _inner.DeleteExpiredOrCompletedAsync(
            expirationCutoff,
            completedCutoff,
            batchSize,
            cancellationToken);
    }
}
