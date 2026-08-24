using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Constants;

/// <summary>
/// Defines authentication schemes used by the Google sign-in flow.
/// </summary>
[ExcludeFromCodeCoverage]
public static class GoogleAuthenticationSchemes
{
    /// <summary>
    /// Identifies the remote Google OpenID Connect scheme.
    /// </summary>
    public const string OpenIdConnect = "GoogleOidc";

    /// <summary>
    /// Identifies the short-lived protected external identity cookie.
    /// </summary>
    public const string ExternalCookie = "GoogleExternal";
}
