using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Options;
/// <summary>
/// Represents web security options.
/// </summary>

[ExcludeFromCodeCoverage]
public class WebSecurityOptions
{
    /// <summary>
    /// Identifies section name.
    /// </summary>
    public const string SectionName = "WebSecurity";
    /// <summary>
    /// Identifies antiforgery header name.
    /// </summary>

    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";
    /// <summary>
    /// Gets allowed origins.
    /// </summary>

    public string[] AllowedOrigins { get; init; } = [];

}
