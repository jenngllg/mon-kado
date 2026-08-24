namespace JennGllg.Fr.MonKado.Back.Api.Abstractions;

/// <summary>
/// Validates canonical frontend return paths.
/// </summary>
public interface IGoogleReturnPathValidator
{
    /// <summary>
    /// Determines whether a path is a canonical relative frontend path.
    /// </summary>
    /// <param name="returnPath">The path to validate.</param>
    /// <returns><see langword="true" /> when the path is canonical.</returns>
    bool IsCanonical(string? returnPath);
}
