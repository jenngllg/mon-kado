using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using SkiaSharp;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.Services;

/// <summary>Composes normalized product images on an ivory social-preview canvas.</summary>
public class WishlistSharePreviewImageProcessor : IWishlistSharePreviewImageProcessor
{
    private const int Width = 1200;
    private const int Height = 630;
    private const int MaximumImages = 2;
    private const int Padding = 24;
    private const int JpegQuality = 85;

    /// <inheritdoc />
    public byte[] Compose(
        IReadOnlyList<ReadOnlyMemory<byte>> images,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            images.Count,
            1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            images.Count,
            MaximumImages);
        cancellationToken.ThrowIfCancellationRequested();
        using var surface = SKSurface.Create(new SKImageInfo(
            Width,
            Height));
        surface.Canvas.Clear(new SKColor(
            250,
            249,
            245));
        var cellWidth = Width / images.Count;

        for (var index = 0; index < images.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var data = SKData.CreateCopy(images[index].Span);
            using var image = SKImage.FromEncodedData(data)
                ?? throw new InvalidOperationException("A normalized preview image is corrupt.");
            var scale = Math.Min(
                (float)(cellWidth - Padding * 2) / image.Width,
                (float)(Height - Padding * 2) / image.Height);
            var width = image.Width * scale;
            var height = image.Height * scale;
            var left = index * cellWidth + (cellWidth - width) / 2;
            var top = (Height - height) / 2;
            surface.Canvas.DrawImage(
                image,
                SKRect.Create(
                    left,
                    top,
                    width,
                    height),
                new SKSamplingOptions(
                    SKFilterMode.Linear,
                    SKMipmapMode.None));
        }
        using var composed = surface.Snapshot();
        using var encoded = composed.Encode(
            SKEncodedImageFormat.Jpeg,
            JpegQuality);
        cancellationToken.ThrowIfCancellationRequested();

        return encoded.ToArray();
    }
}
