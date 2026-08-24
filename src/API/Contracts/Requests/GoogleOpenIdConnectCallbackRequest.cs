using Microsoft.AspNetCore.Mvc;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Describes the form-post response consumed by the OpenID Connect middleware.
/// </summary>
[ExcludeFromCodeCoverage]
public class GoogleOpenIdConnectCallbackRequest
{
    /// <summary>
    /// Gets the short-lived authorization code returned after consent.
    /// </summary>
    public string? Code
    {
        get; init;
    }

    /// <summary>
    /// Gets the protected correlation state returned by the provider.
    /// </summary>
    public string? State
    {
        get; init;
    }

    /// <summary>
    /// Gets the provider protocol error when authorization did not complete.
    /// </summary>
    public string? Error
    {
        get; init;
    }

    /// <summary>
    /// Gets the optional provider error description, which is never exposed or logged.
    /// </summary>
    [FromForm(Name = "error_description")]
    public string? ErrorDescription
    {
        get; init;
    }
}
