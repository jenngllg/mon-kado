using Microsoft.AspNetCore.Authorization;

namespace JennGllg.Fr.MonKado.Back.Api.Authorization;

/// <summary>
/// Requires the authenticated member to own a private wishlist.
/// </summary>
public class WishlistOwnerRequirement : IAuthorizationRequirement
{
    /// <summary>Gets whether the owner must also be allowed to change the wishlist.</summary>
    public bool RequiresWritable
    {
        get; init;
    }
}
