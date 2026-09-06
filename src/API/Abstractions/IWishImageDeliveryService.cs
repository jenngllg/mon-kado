using JennGllg.Fr.MonKado.Back.Api.Models;

namespace JennGllg.Fr.MonKado.Back.Api.Abstractions;

/// <summary>
/// Revalidates signed image grants and opens current normalized image content.
/// </summary>
public interface IWishImageDeliveryService
{
    /// <summary>
    /// Opens an image after revalidating an owner-scoped grant.
    /// </summary>
    /// <param name="grant">The validated owner grant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The readable WebP stream.</returns>
    Task<Stream> OpenOwnedAsync(
        WishImageGrant grant,
        CancellationToken cancellationToken);

    /// <summary>
    /// Opens an image after revalidating a share-link-scoped grant.
    /// </summary>
    /// <param name="grant">The validated shared grant.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The readable WebP stream.</returns>
    Task<Stream> OpenSharedAsync(
        WishImageGrant grant,
        CancellationToken cancellationToken);
}
