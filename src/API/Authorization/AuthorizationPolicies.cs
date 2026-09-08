using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Authorization;

/// <summary>
/// Defines named API authorization policies.
/// </summary>
[ExcludeFromCodeCoverage]
public static class AuthorizationPolicies
{
    #region Account

    /// <summary>
    /// Identifies the policy for an authenticated current member session.
    /// </summary>
    public const string CurrentSession = "CurrentSession";

    /// <summary>Allows database-authorized administrators to request and download member data exports.</summary>
    public const string ExportMemberData = "ExportMemberData";

    /// <summary>Allows database-authorized administrators to erase another member's account.</summary>
    public const string EraseMemberData = "EraseMemberData";

    #endregion

    #region Wishlist

    /// <summary>
    /// Identifies the policy for managing an owned private wishlist.
    /// </summary>
    public const string ManageWishlist = "ManageWishlist";

    /// <summary>Identifies writable owner access, excluding administratively suspended wishlists.</summary>
    public const string ModifyWishlist = "ModifyWishlist";

    /// <summary>Identifies the database-backed administrator moderation policy.</summary>
    public const string ModerateWishlist = "ModerateWishlist";

    /// <summary>Identifies database-backed administrator access to reported wishlists.</summary>
    public const string ViewWishlistReports = "ViewWishlistReports";

    /// <summary>Allows PostgreSQL-authorized administrators to review reports.</summary>
    public const string ProcessWishlistReports = "ProcessWishlistReports";

    #endregion
}
