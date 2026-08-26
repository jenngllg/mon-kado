namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents an unavailable owner-facing wishlist share link.
/// </summary>
public class WishlistShareLinkNotFoundException()
    : Exception("The wishlist share link was not found.");
