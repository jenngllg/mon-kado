using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>
/// Validates canonical frontend return paths without decoding attacker-controlled input.
/// </summary>
public class GoogleReturnPathValidator : IGoogleReturnPathValidator
{
    /// <inheritdoc />
    public bool IsCanonical(string? returnPath)
    {

        if (string.IsNullOrEmpty(returnPath) ||
            returnPath.Length > GoogleReturnPathValidation.MaximumLength ||
            returnPath[0] != '/' ||
            returnPath.StartsWith(
                "//",
                StringComparison.Ordinal) ||
            returnPath.Contains(
                '\\',
                StringComparison.Ordinal) ||
            returnPath.Contains(
                '%',
                StringComparison.Ordinal) ||
            returnPath.Contains(
                '?',
                StringComparison.Ordinal) ||
            returnPath.Contains(
                '#',
                StringComparison.Ordinal) ||
            returnPath.Any(char.IsControl) ||
            returnPath.Any(char.IsWhiteSpace))
            return false;

        var segments = returnPath.Split('/');

        return segments
            .Skip(1)
            .All(segment => segment.Length > 0 && segment is not "." and not "..") ||
            returnPath == "/";
    }
}
