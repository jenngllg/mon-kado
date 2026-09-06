using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Constants;

/// <summary>
/// Defines the public validation and normalization limits for gift images.
/// </summary>
[ExcludeFromCodeCoverage]
public static class GiftImageConstraints
{
    /// <summary>
    /// Gets the maximum accepted source length in bytes.
    /// </summary>
    public const int MaximumInputLength = 10 * 1024 * 1024;

    /// <summary>
    /// Gets the maximum accepted decoded pixel count.
    /// </summary>
    public const long MaximumPixelCount = 40_000_000;

    /// <summary>
    /// Gets the maximum normalized width or height.
    /// </summary>
    public const int MaximumOutputEdgeLength = 1600;

    /// <summary>
    /// Gets the normalized lossy WebP quality.
    /// </summary>
    public const int WebpQuality = 82;
}
