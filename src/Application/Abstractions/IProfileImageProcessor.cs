using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Normalizes untrusted profile photos without exposing native image types.</summary>
public interface IProfileImageProcessor
{
    /// <summary>Validates and normalizes a profile photo to a bounded WebP image.</summary>
    /// <param name="content">The untrusted image bytes.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The normalized bytes and their SHA-256 hash.</returns>
    /// <exception cref="ProfileImageUnsupportedFormatException">The actual image format is unsupported.</exception>
    /// <exception cref="ProfileImageInvalidException">The image is invalid or exceeds the decoding limits.</exception>
    Task<ProcessedGiftImage> ProcessAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken);
}
