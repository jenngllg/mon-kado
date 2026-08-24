namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Identifies the current member access to a private wishlist.
/// </summary>
public enum WishlistAccess
{
    /// <summary>
    /// The authenticated member no longer exists.
    /// </summary>
    MemberNotFound,

    /// <summary>
    /// The wishlist does not exist or belongs to another member.
    /// </summary>
    NotOwned,

    /// <summary>
    /// The member owns the wishlist.
    /// </summary>
    Owner
}
