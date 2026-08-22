namespace JennGllg.Fr.MonKado.Back.Api.Abstractions;

/// <summary>
/// Formats and validates member profile entity tags.
/// </summary>
public interface IEntityTagService
{
    /// <summary>
    /// Formats a member profile version as a strong entity tag.
    /// </summary>
    /// <param name="version">The member profile version.</param>
    /// <returns>The strong entity tag.</returns>
    string Format(uint version);

    /// <summary>
    /// Parses a required strong entity tag.
    /// </summary>
    /// <param name="value">The raw If-Match header value.</param>
    /// <returns>The member profile version.</returns>
    /// <exception cref="Application.Common.Exceptions.PreconditionRequiredException">
    /// The If-Match header is absent.
    /// </exception>
    /// <exception cref="Application.Common.Exceptions.RequestValidationException">
    /// The If-Match header is malformed.
    /// </exception>
    uint Parse(string? value);
}
