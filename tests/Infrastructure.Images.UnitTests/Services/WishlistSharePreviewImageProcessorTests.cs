using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Services;

using SkiaSharp;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.UnitTests.Services;

public class WishlistSharePreviewImageProcessorTests
{
    private readonly WishlistSharePreviewImageProcessor _processor = new();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Compose_WhenNormalizedImagesProvided_ReturnsBoundedJpegWithoutCropping(int count)
    {
        // Arrange
        var images = Enumerable.Range(
                0,
                count)
            .Select(index => CreateImage(index == 0 ? SKColors.Red : SKColors.Blue))
            .ToArray();

        // Act
        var bytes = _processor.Compose(
            images,
            TestContext.Current.CancellationToken);

        // Assert
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        using var image = SKBitmap.Decode(bytes);
        Assert.Equal(
            SKEncodedImageFormat.Jpeg,
            codec.EncodedFormat);
        Assert.Equal(
            1200,
            image.Width);
        Assert.Equal(
            630,
            image.Height);
        var first = image.GetPixel(
            count == 1 ? 600 : 300,
            315);
        Assert.True(first.Red > 240 && first.Blue < 15);
        var padding = image.GetPixel(
            0,
            0);
        Assert.True(padding.Red > 240 && padding.Green > 240 && padding.Blue > 240);

        if (count == 2)
        {
            var second = image.GetPixel(
                900,
                315);
            Assert.True(second.Blue > 240 && second.Red < 15);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Compose_WhenImageCountOutsideBound_ThrowsArgumentOutOfRange(int count)
    {
        // Arrange
        var images = new ReadOnlyMemory<byte>[count];

        // Act
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => _processor.Compose(
            images,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.NotNull(exception);
    }

    [Fact]
    public void Compose_WhenImageCorrupt_ThrowsWithoutReturningImage()
    {
        // Arrange
        ReadOnlyMemory<byte>[] images = [new byte[] { 1 }];

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() => _processor.Compose(
            images,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.Contains(
            "corrupt",
            exception.Message);
    }

    [Fact]
    public void Compose_WhenCanceled_ThrowsOperationCanceled()
    {
        // Arrange
        ReadOnlyMemory<byte>[] images = [CreateImage(SKColors.Red)];
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var exception = Assert.Throws<OperationCanceledException>(() => _processor.Compose(
            images,
            cancellation.Token));

        // Assert
        Assert.Equal(
            cancellation.Token,
            exception.CancellationToken);
    }

    private static ReadOnlyMemory<byte> CreateImage(SKColor color)
    {
        using var bitmap = new SKBitmap(
            80,
            160);
        bitmap.Erase(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(
            SKEncodedImageFormat.Webp,
            100);

        return data.ToArray();
    }
}
