using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore.Storage;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Adapts an EF Core transaction for wishlist participant operations.
/// </summary>
/// <param name="transaction">The EF Core transaction.</param>
public class WishlistParticipantTransaction(IDbContextTransaction transaction)
    : IWishlistParticipantTransaction
{
    /// <inheritdoc />
    public Task CommitAsync(CancellationToken cancellationToken)
    {
        return transaction.CommitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
