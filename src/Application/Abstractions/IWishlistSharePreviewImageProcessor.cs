namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Composes at most two normalized product images into a social-preview image.</summary>
public interface IWishlistSharePreviewImageProcessor
{
    /// <summary>Fits images without cropping or stretching into a bounded JPEG canvas.</summary>
    /// <param name="images">One or two already normalized image containers.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The encoded JPEG image.</returns>
    byte[] Compose(
        IReadOnlyList<ReadOnlyMemory<byte>> images,
        CancellationToken cancellationToken);
}
