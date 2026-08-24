using Microsoft.AspNetCore.Authorization;

namespace JennGllg.Fr.MonKado.Back.Api.Authorization;

/// <summary>
/// Requires the authenticated member to own a private wishlist.
/// </summary>
public class WishlistOwnerRequirement : IAuthorizationRequirement
{
}
