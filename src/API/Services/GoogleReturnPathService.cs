using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>
/// Resolves exact allowlisted frontend redirects for Google authentication.
/// </summary>
public class GoogleReturnPathService(
    IOptions<GoogleAuthenticationOptions> options,
    IGoogleReturnPathValidator validator) : IGoogleReturnPathService
{
    private readonly GoogleAuthenticationOptions _options = options.Value;

    /// <inheritdoc />
    public string Resolve(string? returnPath)
    {
        var candidate = returnPath ?? _options.DefaultReturnPath;

        if (candidate is null ||
            !validator.IsCanonical(candidate) ||
            _options.AllowedReturnPaths?.Contains(
                candidate,
                StringComparer.Ordinal) != true)
            throw new RequestValidationException(
            [
                new ValidationError(
                    "returnPath",
                    "The returnPath must be an allowed relative frontend path.")
            ]);

        return candidate;
    }

    /// <inheritdoc />
    public string BuildAbsoluteUri(string returnPath)
    {

        return string.Concat(
            _options.FrontendOrigin,
            returnPath);
    }
}
