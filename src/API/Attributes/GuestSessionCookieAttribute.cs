namespace JennGllg.Fr.MonKado.Back.Api.Attributes;

/// <summary>
/// Documents an endpoint that reads or creates the browser guest-session cookie.
/// </summary>
/// <param name="isRequired">Whether the cookie is required by the HTTP contract.</param>
/// <param name="isReturned">Whether a successful response can set the cookie.</param>
[AttributeUsage(AttributeTargets.Method)]
public class GuestSessionCookieAttribute(
    bool isRequired,
    bool isReturned = false) : Attribute
{
    /// <summary>Gets whether the cookie is required.</summary>
    public bool IsRequired { get; } = isRequired;

    /// <summary>Gets whether a successful response can set the cookie.</summary>
    public bool IsReturned { get; } = isReturned;
}
