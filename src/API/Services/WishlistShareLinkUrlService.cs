using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Options;

using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>
/// Builds copyable frontend wishlist share URLs.
/// </summary>
/// <param name="options">The validated wishlist-sharing options.</param>
public class WishlistShareLinkUrlService(IOptions<WishlistSharingOptions> options)
    : IWishlistShareLinkUrlService
{
    private readonly string _frontendOrigin = options.Value.FrontendOrigin
        ?? throw new InvalidOperationException("WishlistSharing:FrontendOrigin is required.");

    /// <inheritdoc />
    public string Build(
        Guid shareLinkId,
        string secret)
    {
        return $"{_frontendOrigin}/#/shared-wishlists/{shareLinkId:N}.{secret}";
    }
}
