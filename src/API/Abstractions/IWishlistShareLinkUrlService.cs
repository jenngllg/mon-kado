namespace JennGllg.Fr.MonKado.Back.Api.Abstractions;

/// <summary>
/// Builds copyable frontend URLs for wishlist share links.
/// </summary>
public interface IWishlistShareLinkUrlService
{
    /// <summary>Builds the frontend URL for a share-link identifier and secret.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="secret">The bearer secret.</param>
    /// <returns>The absolute copyable URL.</returns>
    string Build(
        Guid shareLinkId,
        string secret);
}
