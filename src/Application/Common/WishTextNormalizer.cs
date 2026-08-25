using System.Text;

namespace JennGllg.Fr.MonKado.Back.Application.Common;

/// <summary>
/// Normalizes wish text after centralized validation.
/// </summary>
public static class WishTextNormalizer
{
    /// <summary>
    /// Normalizes a wish name for display.
    /// </summary>
    /// <param name="name">The validated name.</param>
    /// <returns>The normalized display name.</returns>
    public static string NormalizeName(string name)
    {
        return name
            .Trim()
            .Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Normalizes an optional wish note.
    /// </summary>
    /// <param name="note">The validated optional note.</param>
    /// <returns>The normalized note, or <see langword="null" /> when it is blank.</returns>
    public static string? NormalizeNote(string? note)
    {
        var normalizedNote = note?
            .Trim()
            .Normalize(NormalizationForm.FormC);

        return string.IsNullOrEmpty(normalizedNote)
            ? null
            : normalizedNote;
    }

    /// <summary>
    /// Normalizes an optional wish URL.
    /// </summary>
    /// <param name="url">The validated optional URL.</param>
    /// <returns>The trimmed URL, or <see langword="null" /> when it is blank.</returns>
    public static string? NormalizeUrl(string? url)
    {
        var normalizedUrl = url?.Trim();

        return string.IsNullOrEmpty(normalizedUrl)
            ? null
            : normalizedUrl;
    }
}
