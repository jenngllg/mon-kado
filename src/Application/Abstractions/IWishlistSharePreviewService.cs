using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Reads the allowlisted public metadata of an actively shared wishlist.</summary>
public interface IWishlistSharePreviewService
{
    /// <summary>Reads a preview without accepting or disclosing any share secret.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The minimal preview, or null when public sharing is unavailable.</returns>
    Task<WishlistSharePreview?> GetAsync(
        Guid shareLinkId,
        CancellationToken cancellationToken);
}
