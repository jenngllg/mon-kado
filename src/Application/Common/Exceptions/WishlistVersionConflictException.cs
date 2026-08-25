namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>
/// Represents an optimistic concurrency conflict while updating a wishlist.
/// </summary>
public class WishlistVersionConflictException()
    : Exception("The wishlist has changed since it was retrieved.");
