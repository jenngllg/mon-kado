using System.Buffers;
using System.Globalization;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates Unicode text and URLs used by gift wishes.
/// </summary>
public static class WishTextValidation
{
    /// <summary>
    /// Identifies the maximum wish name length in Unicode scalar values.
    /// </summary>
    public const int MaximumNameLength = 100;

    /// <summary>
    /// Identifies the maximum wish note length in Unicode scalar values.
    /// </summary>
    public const int MaximumNoteLength = 500;

    /// <summary>
    /// Identifies the maximum wish URL length in Unicode scalar values.
    /// </summary>
    public const int MaximumUrlLength = 2048;

    /// <summary>
    /// Identifies the maximum number of digits in a wish price.
    /// </summary>
    public const int MaximumPricePrecision = 10;

    /// <summary>
    /// Identifies the maximum number of fractional digits in a wish price.
    /// </summary>
    public const int MaximumPriceScale = 2;

    /// <summary>
    /// Identifies the minimum total desired quantity.
    /// </summary>
    public const int MinimumQuantity = 1;

    /// <summary>
    /// Identifies the maximum total desired quantity.
    /// </summary>
    public const int MaximumQuantity = 100;

    /// <summary>
    /// Determines whether a required wish name is valid.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true" /> when the name is valid.</returns>
    public static bool IsValidName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !IsWellFormed(value))
            return false;

        var candidate = value.Trim();

        return candidate.EnumerateRunes().Count() <= MaximumNameLength &&
            candidate.EnumerateRunes().All(rune =>
                Rune.GetUnicodeCategory(rune) is not (
                    UnicodeCategory.Control or
                    UnicodeCategory.LineSeparator or
                    UnicodeCategory.ParagraphSeparator));
    }

    /// <summary>
    /// Determines whether an optional wish note is valid.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true" /> when the note is valid.</returns>
    public static bool IsValidNote(string? value)
    {
        if (value is null)
            return true;

        if (!IsWellFormed(value) || value.Trim().EnumerateRunes().Count() > MaximumNoteLength)
            return false;

        return value.EnumerateRunes().All(rune =>
            Rune.GetUnicodeCategory(rune) is not UnicodeCategory.Control ||
            rune.Value is '\r' or '\n' or '\t');
    }

    /// <summary>
    /// Determines whether an optional absolute HTTP or HTTPS URL is valid.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true" /> when the URL is valid.</returns>
    public static bool IsValidUrl(string? value)
    {
        if (value is null)
            return true;

        var candidate = value.Trim();

        if (candidate.Length == 0)
            return true;

        if (!IsWellFormed(candidate) ||
            candidate.EnumerateRunes().Count() > MaximumUrlLength ||
            !Uri.IsWellFormedUriString(
                candidate,
                UriKind.Absolute) ||
            !Uri.TryCreate(
                candidate,
                UriKind.Absolute,
                out var uri))
        {
            return false;
        }

        var authority = uri.GetComponents(
            UriComponents.StrongAuthority,
            UriFormat.UriEscaped);

        return (string.Equals(
                uri.Scheme,
                Uri.UriSchemeHttp,
                StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    uri.Scheme,
                    Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase)) &&
            !string.IsNullOrWhiteSpace(uri.Host) &&
            !authority.Contains(
                '@',
                StringComparison.Ordinal);
    }

    private static bool IsWellFormed(string value)
    {
        var remaining = value.AsSpan();

        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(
                remaining,
                out _,
                out var charactersConsumed);

            if (status is not OperationStatus.Done)
                return false;

            remaining = remaining[charactersConsumed..];
        }

        return true;
    }
}
