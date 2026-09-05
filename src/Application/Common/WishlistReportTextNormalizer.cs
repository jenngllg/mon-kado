using System.Text;

namespace JennGllg.Fr.MonKado.Back.Application.Common;

/// <summary>
/// Normalizes optional visitor-provided wishlist report text after validation.
/// </summary>
public static class WishlistReportTextNormalizer
{
    /// <summary>
    /// Normalizes optional report details.
    /// </summary>
    /// <param name="details">The validated optional details.</param>
    /// <returns>The normalized details, or <see langword="null" /> when blank.</returns>
    public static string? NormalizeDetails(string? details)
    {
        var normalizedDetails = details?
            .Trim()
            .Normalize(NormalizationForm.FormC);

        return string.IsNullOrEmpty(normalizedDetails)
            ? null
            : normalizedDetails;
    }
}
