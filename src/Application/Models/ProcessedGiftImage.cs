namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents normalized WebP image content and its SHA-256 hash.
/// </summary>
/// <param name="content">The normalized WebP bytes.</param>
/// <param name="contentHash">The SHA-256 hash of the normalized bytes.</param>
public class ProcessedGiftImage(
    ReadOnlyMemory<byte> content,
    byte[] contentHash)
{
    private readonly byte[] _content = content.ToArray();
    private readonly byte[] _contentHash = contentHash.ToArray();

    /// <summary>
    /// Gets the normalized WebP bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Content => _content;

    /// <summary>
    /// Gets the SHA-256 content hash.
    /// </summary>
    public byte[] ContentHash => [.. _contentHash];
}
