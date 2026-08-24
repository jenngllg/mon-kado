using System.Text;

namespace JennGllg.Fr.MonKado.Back.Application.Common;

/// <summary>
/// Normalizes wishlist text after centralized validation.
/// </summary>
public static class WishlistTextNormalizer
{
    /// <summary>
    /// Normalizes a wishlist name for display.
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
    /// Normalizes a wishlist name for owner-scoped uniqueness.
    /// </summary>
    /// <param name="name">The normalized display name.</param>
    /// <returns>The normalized uniqueness key.</returns>
    public static string NormalizeNameForUniqueness(string name)
    {
        return name
            .ToUpperInvariant()
            .Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Normalizes an optional wishlist message.
    /// </summary>
    /// <param name="message">The validated optional message.</param>
    /// <returns>The normalized message, or <see langword="null" /> when it is blank.</returns>
    public static string? NormalizeMessage(string? message)
    {
        var normalizedMessage = message?
            .Trim()
            .Normalize(NormalizationForm.FormC);

        return string.IsNullOrEmpty(normalizedMessage)
            ? null
            : normalizedMessage;
    }
}
