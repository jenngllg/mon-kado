namespace JennGllg.Fr.MonKado.Back.Api.Attributes;

/// <summary>
/// Marks a successful response that returns a Cache-Control no-store header.
/// </summary>
/// <param name="statusCode">The documented response status code.</param>
[AttributeUsage(
    AttributeTargets.Method,
    AllowMultiple = true)]
public class NoStoreResponseAttribute(int statusCode) : Attribute
{
    /// <summary>
    /// Gets the documented response status code.
    /// </summary>
    public int StatusCode { get; } = statusCode;
}
