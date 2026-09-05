using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Services;

using SkiaSharp;

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.UnitTests.Services;

public class GiftImageProcessorTests
{
    private readonly GiftImageProcessor _processor = new();

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public async Task ProcessAsync_WhenFormatIsSupported_ReturnsNormalizedWebp(
        SKEncodedImageFormat format)
    {
        // Arrange
        var content = CreateImage(
            12,
            8,
            format,
            SKColors.CornflowerBlue);

        // Act
        var result = await _processor.ProcessAsync(
            content,
            TestContext.Current.CancellationToken);

        // Assert
        using var data = SKData.CreateCopy(result.Content.Span);
        using var codec = SKCodec.Create(data);
        Assert.NotNull(codec);
        Assert.Equal(
            SKEncodedImageFormat.Webp,
            codec.EncodedFormat);
        Assert.Equal(
            12,
            codec.Info.Width);
        Assert.Equal(
            8,
            codec.Info.Height);
        Assert.Equal(
            SHA256.HashData(result.Content.Span),
            result.ContentHash);
    }

    [Fact]
    public async Task ProcessAsync_WhenImageIsSmallerThanLimit_DoesNotEnlargeImage()
    {
        // Arrange
        var content = CreateImage(
            320,
            180,
            SKEncodedImageFormat.Png,
            SKColors.Blue);

        // Act
        var result = await _processor.ProcessAsync(
            content,
            TestContext.Current.CancellationToken);

        // Assert
        using var bitmap = SKBitmap.Decode(result.Content.ToArray());
        Assert.NotNull(bitmap);
        Assert.Equal(
            320,
            bitmap.Width);
        Assert.Equal(
            180,
            bitmap.Height);
    }

    [Fact]
    public async Task ProcessAsync_WhenImageExceedsEdgeLimit_PreservesRatioAndResizes()
    {
        // Arrange
        var content = CreateImage(
            2000,
            1000,
            SKEncodedImageFormat.Jpeg,
            SKColors.Green);

        // Act
        var result = await _processor.ProcessAsync(
            content,
            TestContext.Current.CancellationToken);

        // Assert
        using var bitmap = SKBitmap.Decode(result.Content.ToArray());
        Assert.NotNull(bitmap);
        Assert.Equal(
            GiftImageConstraints.MaximumOutputEdgeLength,
            bitmap.Width);
        Assert.Equal(
            800,
            bitmap.Height);
    }

    [Fact]
    public async Task ProcessAsync_WhenPngContainsTransparency_PreservesAlpha()
    {
        // Arrange
        using var bitmap = new SKBitmap(
            2,
            2,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        bitmap.Erase(SKColors.Transparent);
        bitmap.SetPixel(
            0,
            0,
            SKColors.Red);
        var content = Encode(
            bitmap,
            SKEncodedImageFormat.Png);

        // Act
        var result = await _processor.ProcessAsync(
            content,
            TestContext.Current.CancellationToken);

        // Assert
        using var normalized = SKBitmap.Decode(result.Content.ToArray());
        Assert.NotNull(normalized);
        Assert.True(normalized.GetPixel(
            1,
            1).Alpha <= 1);
        Assert.True(normalized.GetPixel(
            0,
            0).Alpha > 0);
    }

    [Theory]
    [InlineData(1, 2, 1)]
    [InlineData(2, 2, 1)]
    [InlineData(3, 2, 1)]
    [InlineData(4, 2, 1)]
    [InlineData(5, 1, 2)]
    [InlineData(6, 1, 2)]
    [InlineData(7, 1, 2)]
    [InlineData(8, 1, 2)]
    public async Task ProcessAsync_WhenJpegContainsExifOrientation_AppliesOrientationAndStripsMetadata(
        int orientation,
        int expectedWidth,
        int expectedHeight)
    {
        // Arrange
        using var bitmap = new SKBitmap(
            2,
            1,
            SKColorType.Rgba8888,
            SKAlphaType.Opaque);
        bitmap.SetPixel(
            0,
            0,
            SKColors.Red);
        bitmap.SetPixel(
            1,
            0,
            SKColors.Blue);
        var jpeg = Encode(
            bitmap,
            SKEncodedImageFormat.Jpeg);
        var orientedJpeg = AddExifOrientation(
            jpeg,
            (ushort)orientation);

        // Act
        var result = await _processor.ProcessAsync(
            orientedJpeg,
            TestContext.Current.CancellationToken);

        // Assert
        using var normalized = SKBitmap.Decode(result.Content.ToArray());
        Assert.NotNull(normalized);
        Assert.Equal(
            expectedWidth,
            normalized.Width);
        Assert.Equal(
            expectedHeight,
            normalized.Height);
        Assert.DoesNotContain(
            "Exif",
            Encoding.Latin1.GetString(result.Content.Span));
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("svg")]
    [InlineData("heic")]
    public async Task ProcessAsync_WhenFormatIsUnsupported_ThrowsUnsupportedFormat(string format)
    {
        // Arrange
        var content = format switch
        {
            "gif" => "GIF89a"u8.ToArray(),
            "svg" => "<svg></svg>"u8.ToArray(),
            _ => "\0\0\0\x18ftypheic"u8.ToArray()
        };

        // Act
        var exception = await Record.ExceptionAsync(() => _processor.ProcessAsync(
            content,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GiftImageUnsupportedFormatException>(exception);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task ProcessAsync_WhenContainerIsAnimated_ThrowsUnsupportedFormat(string format)
    {
        // Arrange
        var content = format == "png"
            ? CreateAnimatedPngHeader()
            : CreateAnimatedWebpHeader();

        // Act
        var exception = await Record.ExceptionAsync(() => _processor.ProcessAsync(
            content,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GiftImageUnsupportedFormatException>(exception);
    }

    [Fact]
    public async Task ProcessAsync_WhenSupportedSignatureIsCorrupt_ThrowsInvalidImage()
    {
        // Arrange
        byte[] content =
        [
            0xFF,
            0xD8,
            0xFF,
            0x00
        ];

        // Act
        var exception = await Record.ExceptionAsync(() => _processor.ProcessAsync(
            content,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GiftImageInvalidException>(exception);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task ProcessAsync_WhenAnimationChunkIsTruncated_ThrowsInvalidImage(string format)
    {
        // Arrange
        var content = format == "png"
            ? CreateTruncatedPngChunk()
            : CreateTruncatedWebpChunk();

        // Act
        var exception = await Record.ExceptionAsync(() => _processor.ProcessAsync(
            content,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GiftImageInvalidException>(exception);
    }

    [Theory]
    [InlineData("codec")]
    [InlineData("decode")]
    [InlineData("surface")]
    [InlineData("encode")]
    public async Task ProcessAsync_WhenNativeImageOperationFails_ThrowsInvalidImage(string failureStage)
    {
        // Arrange
        var processor = new FailingGiftImageProcessor(failureStage);
        var content = CreateImage(
            2,
            2,
            SKEncodedImageFormat.Png,
            SKColors.Black);

        // Act
        var exception = await Record.ExceptionAsync(() => processor.ProcessAsync(
            content,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GiftImageInvalidException>(exception);
    }

    [Fact]
    public async Task ProcessAsync_WhenImageExceedsPixelLimit_ThrowsInvalidImage()
    {
        // Arrange
        var content = CreateImage(
            1,
            1,
            SKEncodedImageFormat.Png,
            SKColors.Black);
        BinaryPrimitives.WriteInt32BigEndian(
            content.AsSpan(
                16,
                4),
            8000);
        BinaryPrimitives.WriteInt32BigEndian(
            content.AsSpan(
                20,
                4),
            5001);
        BinaryPrimitives.WriteUInt32BigEndian(
            content.AsSpan(
                29,
                4),
            ComputeCrc32(content.AsSpan(
                12,
                17)));

        // Act
        var exception = await Record.ExceptionAsync(() => _processor.ProcessAsync(
            content,
            TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<GiftImageInvalidException>(exception);
    }

    [Fact]
    public async Task ProcessAsync_WhenCancellationIsRequested_ThrowsOperationCanceled()
    {
        // Arrange
        var content = CreateImage(
            1,
            1,
            SKEncodedImageFormat.Png,
            SKColors.Black);
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();

        // Act
        var exception = await Record.ExceptionAsync(() => _processor.ProcessAsync(
            content,
            cancellationTokenSource.Token));

        // Assert
        Assert.IsAssignableFrom<OperationCanceledException>(exception);
    }

    [Fact]
    public async Task ProcessAsync_WhenSameSourceIsProcessedTwice_ReturnsSameContentAndHash()
    {
        // Arrange
        var content = CreateImage(
            10,
            10,
            SKEncodedImageFormat.Png,
            SKColors.Purple);

        // Act
        var first = await _processor.ProcessAsync(
            content,
            TestContext.Current.CancellationToken);
        var second = await _processor.ProcessAsync(
            content,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            first.Content.ToArray(),
            second.Content.ToArray());
        Assert.Equal(
            first.ContentHash,
            second.ContentHash);
    }

    private static byte[] CreateImage(
        int width,
        int height,
        SKEncodedImageFormat format,
        SKColor color)
    {
        using var bitmap = new SKBitmap(
            width,
            height,
            SKColorType.Rgba8888,
            color.Alpha == byte.MaxValue
                ? SKAlphaType.Opaque
                : SKAlphaType.Premul);
        bitmap.Erase(color);

        return Encode(
            bitmap,
            format);
    }

    private static byte[] Encode(
        SKBitmap bitmap,
        SKEncodedImageFormat format)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(
            format,
            90);

        return data.ToArray();
    }

    private static byte[] AddExifOrientation(
        byte[] jpeg,
        ushort orientation)
    {
        byte[] segment =
        [
            0xFF,
            0xE1,
            0x00,
            0x22,
            0x45,
            0x78,
            0x69,
            0x66,
            0x00,
            0x00,
            0x4D,
            0x4D,
            0x00,
            0x2A,
            0x00,
            0x00,
            0x00,
            0x08,
            0x00,
            0x01,
            0x01,
            0x12,
            0x00,
            0x03,
            0x00,
            0x00,
            0x00,
            0x01,
            (byte)(orientation >> 8),
            (byte)orientation,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00,
            0x00
        ];
        var result = new byte[jpeg.Length + segment.Length];
        jpeg.AsSpan(
            0,
            2).CopyTo(result);
        segment.CopyTo(
            result,
            2);
        jpeg.AsSpan(2).CopyTo(result.AsSpan(2 + segment.Length));

        return result;
    }

    private static byte[] CreateAnimatedPngHeader()
    {
        byte[] content =
        [
            0x89,
            0x50,
            0x4E,
            0x47,
            0x0D,
            0x0A,
            0x1A,
            0x0A,
            0x00,
            0x00,
            0x00,
            0x00,
            0x61,
            0x63,
            0x54,
            0x4C,
            0x00,
            0x00,
            0x00,
            0x00
        ];

        return content;
    }

    private static byte[] CreateAnimatedWebpHeader()
    {
        byte[] content =
        [
            0x52,
            0x49,
            0x46,
            0x46,
            0x0C,
            0x00,
            0x00,
            0x00,
            0x57,
            0x45,
            0x42,
            0x50,
            0x41,
            0x4E,
            0x49,
            0x4D,
            0x00,
            0x00,
            0x00,
            0x00
        ];

        return content;
    }

    private static byte[] CreateTruncatedPngChunk()
    {
        var content = CreateAnimatedPngHeader();
        BinaryPrimitives.WriteUInt32BigEndian(
            content.AsSpan(
                8,
                4),
            uint.MaxValue);
        "IHDR"u8.CopyTo(content.AsSpan(
            12,
            4));

        return content;
    }

    private static byte[] CreateTruncatedWebpChunk()
    {
        var content = CreateAnimatedWebpHeader();
        "VP8 "u8.CopyTo(content.AsSpan(
            12,
            4));
        BinaryPrimitives.WriteUInt32LittleEndian(
            content.AsSpan(
                16,
                4),
            100);

        return content;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> content)
    {
        var crc = uint.MaxValue;

        foreach (var value in content)
        {
            crc ^= value;

            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0
                    ? crc >> 1
                    : (crc >> 1) ^ 0xEDB88320U;
            }
        }

        return ~crc;
    }
}
