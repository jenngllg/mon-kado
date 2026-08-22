namespace JennGllg.Fr.MonKado.Back.Api.Attributes;

/// <summary>
/// Marks an endpoint that reads the rotating refresh token cookie.
/// </summary>
/// <param name="isRequired">Whether the cookie is required by the HTTP contract.</param>
[AttributeUsage(AttributeTargets.Method)]
public class RefreshTokenCookieAttribute(bool isRequired = true) : Attribute
{
    /// <summary>
    /// Gets a value indicating whether the cookie is required by the HTTP contract.
    /// </summary>
    public bool IsRequired { get; } = isRequired;
}
