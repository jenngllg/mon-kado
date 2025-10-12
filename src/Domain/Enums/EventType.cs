namespace JennGllg.Fr.MonKado.Back.Domain.Enums;

/// <summary>
/// Represents the type of event associated with a wishlist.
/// </summary>
public enum EventType
{
    /// <summary>
    /// Event type has not been specified.
    /// </summary>
    Undefined = 0,

    /// <summary>
    /// Represents a birthday event.
    /// </summary>
    Birthday = 1,

    /// <summary>
    /// Represents a wedding event.
    /// </summary>
    Wedding = 2,

    /// <summary>
    /// Represents a generic celebration.
    /// </summary>
    Celebration = 3
}
