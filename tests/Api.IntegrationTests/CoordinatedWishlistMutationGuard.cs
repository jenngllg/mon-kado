using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Pauses an owner mutation immediately before its real database access revalidation.</summary>
/// <param name="guard">The real PostgreSQL mutation guard.</param>
/// <param name="beforeLockAsync">The deterministic test synchronization callback.</param>
public class CoordinatedWishlistMutationGuard(
    IWishlistMutationGuard guard,
    Func<CancellationToken, Task> beforeLockAsync) : IWishlistMutationGuard
{
    /// <inheritdoc/>
    public async Task LockAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        await beforeLockAsync(cancellationToken);
        await guard.LockAsync(
            ownerId,
            wishlistId,
            cancellationToken);
    }
}
