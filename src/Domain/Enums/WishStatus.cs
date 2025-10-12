namespace JennGllg.Fr.MonKado.Back.Domain.Enums;

/// <summary>
/// Represents the lifecycle status of a wish.
/// </summary>
public enum WishStatus
{
    /// <summary>
    /// Status has not been specified.
    /// </summary>
    Undefined = 0,

    /// <summary>
    /// The wish is visible and available.
    /// </summary>
    Available = 1,

    /// <summary>
    /// The wish has been reserved by a guest.
    /// </summary>
    Reserved = 2,

    /// <summary>
    /// The wish has been fulfilled.
    /// </summary>
    Fulfilled = 3
}
