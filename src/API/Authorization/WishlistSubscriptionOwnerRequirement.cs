using Microsoft.AspNetCore.Authorization;

namespace JennGllg.Fr.MonKado.Back.Api.Authorization;

/// <summary>Requires ownership of a currently accessible wishlist subscription.</summary>
public class WishlistSubscriptionOwnerRequirement : IAuthorizationRequirement;
