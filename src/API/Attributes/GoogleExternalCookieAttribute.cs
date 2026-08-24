namespace JennGllg.Fr.MonKado.Back.Api.Attributes;

/// <summary>
/// Marks an endpoint that consumes the protected short-lived Google external cookie.
/// </summary>
/// <param name="isRequired">Whether the cookie is required by the HTTP contract.</param>
[AttributeUsage(AttributeTargets.Method)]
public class GoogleExternalCookieAttribute(bool isRequired = true) : Attribute
{
    /// <summary>
    /// Gets a value indicating whether the cookie is required by the HTTP contract.
    /// </summary>
    public bool IsRequired { get; } = isRequired;
}
