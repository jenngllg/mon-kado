namespace JennGllg.Fr.MonKado.Back.Api.Abstractions;

/// <summary>
/// Resolves and builds allowlisted frontend redirects for Google authentication.
/// </summary>
public interface IGoogleReturnPathService
{
    /// <summary>
    /// Resolves an optional client path to one exact allowlisted path.
    /// </summary>
    /// <param name="returnPath">The optional client-provided path.</param>
    /// <returns>The validated frontend path.</returns>
    /// <exception cref="Application.Common.Exceptions.RequestValidationException">
    /// The provided path is not canonical or allowlisted.
    /// </exception>
    string Resolve(string? returnPath);

    /// <summary>
    /// Builds an absolute redirect URI from a previously validated path.
    /// </summary>
    /// <param name="returnPath">The validated frontend path.</param>
    /// <returns>The absolute frontend URI.</returns>
    string BuildAbsoluteUri(string returnPath);
}
