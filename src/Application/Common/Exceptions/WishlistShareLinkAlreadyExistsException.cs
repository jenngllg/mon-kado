namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents an attempt to create a second active share link for a wishlist.
/// </summary>
public class WishlistShareLinkAlreadyExistsException()
    : Exception("The wishlist already has an active share link.");
