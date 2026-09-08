namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Identifies the existing administrative actions exposed by the global journal.</summary>
public enum AdministrativeAuditAction
{
    /// <summary>Identifies WishlistSuspended.</summary>
    WishlistSuspended,
    /// <summary>Identifies WishlistSuspensionReasonUpdated.</summary>
    WishlistSuspensionReasonUpdated,
    /// <summary>Identifies WishlistReactivated.</summary>
    WishlistReactivated,
    /// <summary>Identifies MemberDataExportRequested.</summary>
    MemberDataExportRequested,
    /// <summary>Identifies MemberDataExportDownloadStarted.</summary>
    MemberDataExportDownloadStarted,
    /// <summary>Identifies MemberErased.</summary>
    MemberErased
}
