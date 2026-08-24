using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Options;

/// <summary>
/// Configures Google OpenID Connect authentication.
/// </summary>
[ExcludeFromCodeCoverage]
public class GoogleAuthenticationOptions
{
    /// <summary>
    /// Gets the minimum supported provider HTTP timeout in seconds.
    /// </summary>
    public const int MinimumBackchannelTimeoutSeconds = 1;

    /// <summary>
    /// Gets the maximum supported provider HTTP timeout in seconds.
    /// </summary>
    public const int MaximumBackchannelTimeoutSeconds = 60;

    /// <summary>
    /// Identifies the configuration section.
    /// </summary>
    public const string SectionName = "GoogleAuthentication";

    /// <summary>
    /// Gets a value indicating whether Google authentication is enabled.
    /// </summary>
    public bool Enabled
    {
        get; init;
    }

    /// <summary>
    /// Gets the confidential web client identifier.
    /// </summary>
    public string? ClientId
    {
        get; init;
    }

    /// <summary>
    /// Gets the confidential web client secret.
    /// </summary>
    public string? ClientSecret
    {
        get; init;
    }

    /// <summary>
    /// Gets the provider discovery, key retrieval and token exchange timeout in seconds.
    /// </summary>
    public int BackchannelTimeoutSeconds { get; init; } = 15;

    /// <summary>
    /// Gets the exact frontend origin used for redirects.
    /// </summary>
    public string? FrontendOrigin
    {
        get; init;
    }

    /// <summary>
    /// Gets the default frontend path used after authentication.
    /// </summary>
    public string? DefaultReturnPath
    {
        get; init;
    }

    /// <summary>
    /// Gets the exact frontend paths allowed after authentication.
    /// </summary>
    public string[]? AllowedReturnPaths
    {
        get; init;
    }
}
