namespace JennGllg.Fr.MonKado.Back.Domain.Enums;

/// <summary>
/// Identifies why a visitor reported a shared wishlist.
/// </summary>
public enum WishlistReportReason
{
    /// <summary>
    /// The wishlist appears to contain spam or support a scam.
    /// </summary>
    SpamOrScam,

    /// <summary>
    /// The wishlist contains inappropriate content.
    /// </summary>
    InappropriateContent,

    /// <summary>
    /// The wishlist appears to disclose private information.
    /// </summary>
    PrivacyViolation,

    /// <summary>
    /// The visitor selected another reason and supplied details.
    /// </summary>
    Other
}
