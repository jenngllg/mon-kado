namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents an optimistic concurrency conflict on a wishlist share link.
/// </summary>
public class WishlistShareLinkVersionConflictException()
    : Exception("The wishlist share link changed before the operation completed.");
