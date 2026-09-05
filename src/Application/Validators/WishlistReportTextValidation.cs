using System.Buffers;
using System.Globalization;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Validates optional visitor-provided wishlist report text.
/// </summary>
public static class WishlistReportTextValidation
{
    /// <summary>
    /// Identifies the maximum report details length in Unicode scalar values.
    /// </summary>
    public const int MaximumDetailsLength = 1000;

    /// <summary>
    /// Determines whether optional report details are valid.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <returns><see langword="true" /> when the value is valid.</returns>
    public static bool IsValidDetails(string? value)
    {

        if (value is null)
            return true;

        if (!IsWellFormed(value) || value.Trim().EnumerateRunes().Count() > MaximumDetailsLength)
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
