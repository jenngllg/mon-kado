namespace JennGllg.Fr.MonKado.Back.Domain.Enums;

/// <summary>Identifies a durable administrator moderation decision.</summary>
public enum WishlistModerationAction
{
    /// <summary>The administrator suspended an active wishlist.</summary>
    Suspended,
    /// <summary>The administrator amended the current suspension reason.</summary>
    ReasonUpdated,
    /// <summary>The administrator reactivated a suspended wishlist.</summary>
    Reactivated
}
