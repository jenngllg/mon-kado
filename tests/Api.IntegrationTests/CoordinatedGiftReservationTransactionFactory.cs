using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Synchronizes a reservation before its real parent-first database lock.</summary>
/// <param name="factory">The real transaction factory.</param>
/// <param name="beforeLockAsync">The deterministic test synchronization callback.</param>
public class CoordinatedGiftReservationTransactionFactory(
    IGiftReservationTransactionFactory factory,
    Func<CancellationToken, Task> beforeLockAsync) : IGiftReservationTransactionFactory
{
    /// <inheritdoc />
    public Task LockOwnedWishlistAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {

        return factory.LockOwnedWishlistAsync(
            ownerId,
            wishlistId,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IGiftReservationTransaction> BeginAsync(CancellationToken cancellationToken)
    {

        return factory.BeginAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task LockMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {

        return factory.LockMemberAsync(
            memberId,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WishlistShareLink?> LockShareLinkAsync(
        Guid shareLinkId,
        CancellationToken cancellationToken)
    {
        await beforeLockAsync(cancellationToken);

        return await factory.LockShareLinkAsync(
            shareLinkId,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Wish?> LockWishAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {

        return factory.LockWishAsync(
            wishlistId,
            wishId,
            cancellationToken);
    }
}
