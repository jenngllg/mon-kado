using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Defines normalization operations for untrusted gift images.
/// </summary>
public interface IGiftImageProcessor
{
    /// <summary>
    /// Validates and normalizes an image to WebP.
    /// </summary>
    /// <param name="content">The untrusted source bytes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalized image.</returns>
    Task<ProcessedGiftImage> ProcessAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}
