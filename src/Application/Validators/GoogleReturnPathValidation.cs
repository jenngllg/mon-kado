namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Provides defense-in-depth validation for protected Google authentication return paths.
/// </summary>
public static class GoogleReturnPathValidation
{
    /// <summary>
    /// Identifies the maximum supported return path length.
    /// </summary>
    public const int MaximumLength = 256;

    /// <summary>
    /// Determines whether a return path is a canonical relative frontend path.
    /// </summary>
    /// <param name="returnPath">The return path.</param>
    /// <returns><see langword="true" /> when the path is canonical.</returns>
    public static bool IsCanonical(string? returnPath)
    {

        if (returnPath is null ||
            returnPath.Length > MaximumLength ||
            !returnPath.StartsWith('/') ||
            returnPath.StartsWith(
                "//",
                StringComparison.Ordinal) ||
            returnPath.Contains(
                "//",
                StringComparison.Ordinal) ||
            returnPath.Contains('%') ||
            returnPath.Contains('\\') ||
            returnPath.Contains('#') ||
            returnPath.Contains('?') ||
            returnPath.Any(char.IsWhiteSpace) ||
            returnPath.Any(char.IsControl))
            return false;

        var segments = returnPath.Split(
            '/');

        return returnPath == "/" ||
            segments
                .Skip(1)
                .All(segment => segment.Length > 0 && segment is not "." and not "..");
    }
}
