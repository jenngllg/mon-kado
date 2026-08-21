namespace JennGllg.Fr.MonKado.Back.Application.Models;
/// <summary>
/// Defines the available authentication email failure category values.
/// </summary>

public enum AuthenticationEmailFailureCategory
{
    /// <summary>
    /// Indicates transient.
    /// </summary>
    Transient,
    /// <summary>
    /// Indicates rate limited.
    /// </summary>
    RateLimited,
    /// <summary>
    /// Indicates authentication.
    /// </summary>
    Authentication,
    /// <summary>
    /// Indicates permission.
    /// </summary>
    Permission,
    /// <summary>
    /// Indicates invalid request.
    /// </summary>
    InvalidRequest,
    /// <summary>
    /// Indicates unknown.
    /// </summary>
    Unknown
}
