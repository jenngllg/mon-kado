namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Defines the bounded set of background operations exposed to local monitoring.</summary>
public enum WorkerOperation
{
    /// <summary>Authentication email dispatch.</summary>
    AuthenticationEmailDelivery,
    /// <summary>Wishlist moderation email dispatch.</summary>
    WishlistModerationEmailDelivery,
    /// <summary>Account erasure processing.</summary>
    AccountErasureProcessing,
    /// <summary>Personal data export processing.</summary>
    PersonalDataExport,
    /// <summary>Unconfirmed account cleanup.</summary>
    UnconfirmedAccountCleanup,
    /// <summary>Expired authentication session cleanup.</summary>
    ExpiredAuthenticationSessionCleanup,
    /// <summary>Expired member email change request cleanup.</summary>
    ExpiredMemberEmailChangeRequestCleanup,
    /// <summary>Processed authentication email cleanup.</summary>
    ProcessedAuthenticationEmailCleanup,
    /// <summary>Expired guest session cleanup.</summary>
    ExpiredGuestSessionCleanup,
    /// <summary>Gift image cleanup.</summary>
    GiftImageCleanup
}
