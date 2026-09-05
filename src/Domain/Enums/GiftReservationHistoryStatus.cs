namespace JennGllg.Fr.MonKado.Back.Domain.Enums;

/// <summary>
/// Describes the current outcome of one member reservation lifecycle.
/// </summary>
public enum GiftReservationHistoryStatus
{
    /// <summary>The reservation is currently active.</summary>
    Active,

    /// <summary>The member cancelled the reservation.</summary>
    Cancelled,

    /// <summary>The reserved gift or its wishlist is no longer available.</summary>
    Unavailable
}
