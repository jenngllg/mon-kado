using JennGllg.Fr.MonKado.Back.Api.Abstractions;

using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Api.Options;

/// <summary>
/// Validates Google authentication configuration before the API starts.
/// </summary>
public class GoogleAuthenticationOptionsValidator(
    IGoogleReturnPathValidator returnPathValidator,
    IOptions<WebSecurityOptions> webSecurityOptions)
    : IValidateOptions<GoogleAuthenticationOptions>
{
    /// <summary>
    /// Validates Google authentication options.
    /// </summary>
    /// <param name="name">The options instance name.</param>
    /// <param name="options">The options to validate.</param>
    /// <returns>The validation result.</returns>
    public ValidateOptionsResult Validate(
        string? name,
        GoogleAuthenticationOptions options)
    {
        _ = name;

        if (!options.Enabled)
            return ValidateOptionsResult.Success;

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ClientId))
            failures.Add("GoogleAuthentication:ClientId is required when Google authentication is enabled.");

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            failures.Add("GoogleAuthentication:ClientSecret is required when Google authentication is enabled.");

        if (options.BackchannelTimeoutSeconds is <
                GoogleAuthenticationOptions.MinimumBackchannelTimeoutSeconds or >
                GoogleAuthenticationOptions.MaximumBackchannelTimeoutSeconds)
            failures.Add(
                "GoogleAuthentication:BackchannelTimeoutSeconds must be between 1 and 60 seconds.");

        var isFrontendOriginValid = ValidateFrontendOrigin(
            options.FrontendOrigin,
            failures);

        if (isFrontendOriginValid &&
            !webSecurityOptions.Value.AllowedOrigins.Contains(
                options.FrontendOrigin,
                StringComparer.Ordinal))
            failures.Add(
                "GoogleAuthentication:FrontendOrigin must be one of WebSecurity:AllowedOrigins.");

        ValidateReturnPaths(
            options,
            failures);

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// Validates the exact HTTPS frontend origin used for post-authentication redirects.
    /// </summary>
    /// <param name="origin">The configured frontend origin.</param>
    /// <param name="failures">The validation failures to populate.</param>
    /// <returns><see langword="true" /> when the origin can be cross-checked with CORS.</returns>
    private static bool ValidateFrontendOrigin(
        string? origin,
        List<string> failures)
    {

        if (string.IsNullOrWhiteSpace(origin) ||
            origin.Contains('*') ||
            !Uri.TryCreate(
                origin,
                UriKind.Absolute,
                out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            origin.EndsWith('/'))
        {
            failures.Add(
                "GoogleAuthentication:FrontendOrigin must contain only an explicit HTTP or HTTPS origin without a trailing slash.");

            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add(
                "GoogleAuthentication:FrontendOrigin must use HTTPS when Google authentication is enabled.");

            return false;
        }

        return true;
    }

    /// <summary>
    /// Validates canonical and exact allowlisted frontend return paths.
    /// </summary>
    /// <param name="options">The Google authentication options.</param>
    /// <param name="failures">The validation failures to populate.</param>
    private void ValidateReturnPaths(
        GoogleAuthenticationOptions options,
        List<string> failures)
    {
        var allowedReturnPaths = options.AllowedReturnPaths;

        if (allowedReturnPaths is null || allowedReturnPaths.Length == 0)
        {
            failures.Add("GoogleAuthentication:AllowedReturnPaths must contain at least one path.");

            return;
        }

        if (allowedReturnPaths.Any(path => !returnPathValidator.IsCanonical(path)))
            failures.Add(
                "GoogleAuthentication:AllowedReturnPaths must contain only canonical relative paths without query strings or fragments.");

        if (allowedReturnPaths.Distinct(StringComparer.Ordinal).Count() !=
            allowedReturnPaths.Length)
            failures.Add("GoogleAuthentication:AllowedReturnPaths cannot contain duplicates.");

        if (!returnPathValidator.IsCanonical(options.DefaultReturnPath) ||
            !allowedReturnPaths.Contains(
                options.DefaultReturnPath,
                StringComparer.Ordinal))
            failures.Add(
                "GoogleAuthentication:DefaultReturnPath must be one of the exact allowed return paths.");
    }
}
