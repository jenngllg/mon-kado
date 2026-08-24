using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Constants;

/// <summary>
/// Defines fixed protocol and navigation values for Google authentication.
/// </summary>
[ExcludeFromCodeCoverage]
public static class GoogleAuthenticationConstants
{
    /// <summary>
    /// Gets the Google OpenID Connect authority.
    /// </summary>
    public const string Authority = "https://accounts.google.com";

    /// <summary>
    /// Gets Google's documented alternate issuer value for ID tokens.
    /// </summary>
    public const string AlternateIssuer = "accounts.google.com";

    /// <summary>
    /// Gets the public callback path handled by OpenID Connect middleware.
    /// </summary>
    public const string CallbackPath = "/api/v1/auth/google/callback";

    /// <summary>
    /// Gets the API completion path used after the remote callback.
    /// </summary>
    public const string CompletionPath = "/api/v1/auth/google/completion";

    /// <summary>
    /// Gets the fixed frontend path used when explicit account linking is required.
    /// </summary>
    public const string LinkPath = "/#/login/link-google";

    /// <summary>
    /// Gets the fixed frontend path used when the Google protocol fails.
    /// </summary>
    public const string AuthenticationFailurePath = "/#/login?error=google_auth_failed";

    /// <summary>
    /// Gets the fixed frontend path used when Google authentication is temporarily unavailable.
    /// </summary>
    public const string AuthenticationUnavailablePath =
        "/#/login?error=google_authentication_unavailable";

    /// <summary>
    /// Gets the fixed frontend path used when an additional local verification is required.
    /// </summary>
    public const string AdditionalVerificationPath =
        "/#/login?error=google_additional_verification_required";

    /// <summary>
    /// Gets the Google login provider name persisted by ASP.NET Core Identity.
    /// </summary>
    public const string LoginProvider = "Google";

    /// <summary>
    /// Gets the external cookie name used outside production.
    /// </summary>
    public const string LocalExternalCookieName = "MonKado.GoogleExternal";

    /// <summary>
    /// Gets the host-prefixed external cookie name used in production.
    /// </summary>
    public const string ProductionExternalCookieName = "__Host-MonKado.GoogleExternal";

    /// <summary>
    /// Gets the protected authentication property containing the frontend return path.
    /// </summary>
    public const string ReturnPathProperty = ".monkado.returnPath";

    /// <summary>
    /// Gets the protected authentication property containing session persistence.
    /// </summary>
    public const string RememberMeProperty = ".monkado.rememberMe";

    /// <summary>
    /// Gets the protected authentication property containing the one-time flow identifier.
    /// </summary>
    public const string FlowIdProperty = ".monkado.flowId";

    /// <summary>
    /// Gets the protected authentication property containing the opaque browser-flow binding.
    /// </summary>
    public const string FlowBindingProperty = ".monkado.flowBinding";

    /// <summary>
    /// Gets the query parameter carrying the opaque browser-flow binding.
    /// </summary>
    public const string FlowBindingParameter = "flow";

    /// <summary>
    /// Gets the protected authentication property containing the member resolved at callback time.
    /// </summary>
    public const string ExpectedMemberIdProperty = ".monkado.expectedMemberId";

    /// <summary>
    /// Gets the protected sentinel indicating that callback-time resolution found no member.
    /// </summary>
    public const string NoExpectedMemberValue = "00000000-0000-0000-0000-000000000000";

    /// <summary>
    /// Gets the protected authentication property containing the proven prior session identifier.
    /// </summary>
    public const string CurrentSessionIdProperty = ".monkado.currentSessionId";

    /// <summary>
    /// Gets the fixed-length protected sentinel indicating that no current session was proven.
    /// </summary>
    public const string NoCurrentSessionValue = "00000000-0000-0000-0000-000000000000";

    /// <summary>
    /// Gets the maximum lifetime of transient Google authentication state.
    /// </summary>
    public static readonly TimeSpan TransientLifetime = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets the accepted protocol clock difference for Google identity tokens.
    /// </summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);
}
