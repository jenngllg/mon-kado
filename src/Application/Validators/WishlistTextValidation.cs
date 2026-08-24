using System.Buffers;
using System.Globalization;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates the Unicode and length rules shared by wishlist text properties.
/// </summary>
public static class WishlistTextValidation
{
    /// <summary>
    /// Identifies the maximum wishlist name length in Unicode scalar values.
    /// </summary>
    public const int MaximumNameLength = 100;

    /// <summary>
    /// Identifies the maximum wishlist message length in Unicode scalar values.
    /// </summary>
    public const int MaximumMessageLength = 500;

    /// <summary>
    /// Determines whether a required wishlist name is valid.
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
    /// Determines whether an optional wishlist message is valid.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true" /> when the message is valid.</returns>
    public static bool IsValidMessage(string? value)
    {
        if (value is null)
            return true;

        if (!IsWellFormed(value) || value.Trim().EnumerateRunes().Count() > MaximumMessageLength)
            return false;

        return value.EnumerateRunes().All(rune =>
            Rune.GetUnicodeCategory(rune) is not UnicodeCategory.Control ||
            rune.Value is '\r' or '\n' or '\t');
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
