namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents a public wishlist that cannot be resolved from a share link.
/// </summary>
public class SharedWishlistNotFoundException()
    : Exception("The shared wishlist was not found.");
