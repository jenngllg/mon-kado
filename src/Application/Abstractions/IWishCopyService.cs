using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Copies an accessible shared wish into a writable owned list.</summary>
public interface IWishCopyService
{
    /// <summary>Creates an independent wish and normalized image without copying reservations.</summary>
    /// <param name="id">The generated destination wish identifier.</param>
    /// <param name="ownerId">The authenticated destination owner.</param>
    /// <param name="wishlistId">The destination list.</param>
    /// <param name="sourceShareLinkId">The source bearer-link identifier.</param>
    /// <param name="sourceWishId">The source wish.</param>
    /// <param name="secret">The source bearer secret.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete created wish.</returns>
    /// <exception cref="SharedWishlistNotFoundException">Source sharing is unavailable or its secret is invalid.</exception>
    /// <exception cref="SharedWishNotFoundException">The source wish no longer exists.</exception>
    /// <exception cref="WishlistNotFoundException">The destination is not owned by this member.</exception>
    /// <exception cref="WishlistArchivedException">The destination is archived.</exception>
    /// <exception cref="WishlistSuspendedException">The destination is suspended.</exception>
    /// <exception cref="WishLimitReachedException">The destination has reached its wish limit.</exception>
    /// <exception cref="GiftImageStorageUnavailableException">The expected image cannot be copied safely.</exception>
    /// <exception cref="DependencyUnavailableException">Persistence is unavailable or the commit outcome is uncertain.</exception>
    /// <exception cref="OperationCanceledException">The operation is cancelled.</exception>
    Task<WishDetails> CopyAsync(
        Guid id,
        Guid ownerId,
        Guid wishlistId,
        Guid sourceShareLinkId,
        Guid sourceWishId,
        string secret,
        CancellationToken cancellationToken);
}
