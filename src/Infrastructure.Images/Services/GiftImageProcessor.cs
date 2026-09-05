using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using SkiaSharp;

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.Services;

/// <summary>
/// Validates untrusted JPEG, PNG, and WebP content and normalizes it to WebP.
/// </summary>
public class GiftImageProcessor : IGiftImageProcessor
{
    private static readonly byte[] _pngSignature =
    [
        0x89,
        0x50,
        0x4E,
        0x47,
        0x0D,
        0x0A,
        0x1A,
        0x0A
    ];

    /// <inheritdoc />
    public Task<ProcessedGiftImage> ProcessAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!HasSupportedSignature(content.Span))
            throw new GiftImageUnsupportedFormatException();

        if (IsAnimatedContainer(content.Span))
            throw new GiftImageUnsupportedFormatException();

        using var data = SKData.CreateCopy(content.Span);
        using var codec = CreateCodec(data);

        if (codec is null)
            throw new GiftImageInvalidException("The supplied gift image is corrupt.");

        var sourceInfo = codec.Info;
        var pixelCount = (long)sourceInfo.Width * sourceInfo.Height;

        if (sourceInfo.Width <= 0 ||
            sourceInfo.Height <= 0 ||
            pixelCount > GiftImageConstraints.MaximumPixelCount)
        {
            throw new GiftImageInvalidException(
                "The supplied gift image dimensions are invalid or too large.");
        }

        using var source = Decode(data);

        if (source is null)
            throw new GiftImageInvalidException("The supplied gift image cannot be decoded.");

        cancellationToken.ThrowIfCancellationRequested();
        var swapsEdges = SwapsEdges(codec.EncodedOrigin);
        var orientedWidth = swapsEdges ? source.Height : source.Width;
        var orientedHeight = swapsEdges ? source.Width : source.Height;
        var scale = Math.Min(
            1D,
            (double)GiftImageConstraints.MaximumOutputEdgeLength /
            Math.Max(
                orientedWidth,
                orientedHeight));
        var outputWidth = Math.Max(
            1,
            (int)Math.Round(
                orientedWidth * scale,
                MidpointRounding.AwayFromZero));
        var outputHeight = Math.Max(
            1,
            (int)Math.Round(
                orientedHeight * scale,
                MidpointRounding.AwayFromZero));
        using var colorSpace = SKColorSpace.CreateSrgb();
        var outputInfo = new SKImageInfo(
            outputWidth,
            outputHeight,
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            colorSpace);
        using var surface = CreateSurface(outputInfo);

        if (surface is null)
            throw new GiftImageInvalidException("The supplied gift image cannot be normalized.");

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.SetMatrix(CreateOrientationMatrix(
            codec.EncodedOrigin,
            source.Width,
            source.Height,
            outputWidth,
            outputHeight));
        canvas.DrawBitmap(
            source,
            0,
            0,
            new SKSamplingOptions(SKCubicResampler.Mitchell),
            null);
        canvas.Flush();
        using var image = surface.Snapshot();
        using var encoded = Encode(image);

        if (encoded is null)
            throw new GiftImageInvalidException("The supplied gift image cannot be encoded.");

        cancellationToken.ThrowIfCancellationRequested();
        var normalizedContent = encoded.ToArray();
        var contentHash = SHA256.HashData(normalizedContent);

        return Task.FromResult(new ProcessedGiftImage(
            normalizedContent,
            contentHash));
    }

    /// <summary>
    /// Creates a native decoder for validated image bytes.
    /// </summary>
    /// <param name="data">The encoded image data.</param>
    /// <returns>The decoder, or <see langword="null" /> when the content cannot be decoded.</returns>
    protected virtual SKCodec? CreateCodec(SKData data)
    {
        return SKCodec.Create(data);
    }

    /// <summary>
    /// Decodes validated image bytes into pixels.
    /// </summary>
    /// <param name="data">The encoded image data.</param>
    /// <returns>The decoded bitmap, or <see langword="null" /> when decoding fails.</returns>
    protected virtual SKBitmap? Decode(SKData data)
    {
        return SKBitmap.Decode(data);
    }

    /// <summary>
    /// Creates the target normalization surface.
    /// </summary>
    /// <param name="imageInfo">The target pixel information.</param>
    /// <returns>The surface, or <see langword="null" /> when native allocation fails.</returns>
    protected virtual SKSurface? CreateSurface(SKImageInfo imageInfo)
    {
        return SKSurface.Create(imageInfo);
    }

    /// <summary>
    /// Encodes normalized pixels as WebP.
    /// </summary>
    /// <param name="image">The normalized image.</param>
    /// <returns>The encoded data, or <see langword="null" /> when native encoding fails.</returns>
    protected virtual SKData? Encode(SKImage image)
    {
        return image.Encode(
            SKEncodedImageFormat.Webp,
            GiftImageConstraints.WebpQuality);
    }

    /// <summary>
    /// Determines whether the content starts with a supported container signature.
    /// </summary>
    /// <param name="content">The untrusted encoded bytes.</param>
    /// <returns><see langword="true" /> for JPEG, PNG, or WebP signatures.</returns>
    private static bool HasSupportedSignature(ReadOnlySpan<byte> content)
    {
        var isJpeg = content.Length >= 3 &&
            content[0] == 0xFF &&
            content[1] == 0xD8 &&
            content[2] == 0xFF;
        var isPng = content.StartsWith(_pngSignature);
        var isWebp = content.Length >= 12 &&
            content[..4].SequenceEqual("RIFF"u8) &&
            content.Slice(
                8,
                4).SequenceEqual("WEBP"u8);

        return isJpeg || isPng || isWebp;
    }

    /// <summary>
    /// Determines whether a supported container declares animation chunks.
    /// </summary>
    /// <param name="content">The untrusted encoded bytes.</param>
    /// <returns><see langword="true" /> when animation is declared.</returns>
    private static bool IsAnimatedContainer(ReadOnlySpan<byte> content)
    {
        return IsAnimatedPng(content) || IsAnimatedWebp(content);
    }

    /// <summary>
    /// Detects the APNG animation control chunk without decoding pixels.
    /// </summary>
    /// <param name="content">The untrusted encoded bytes.</param>
    /// <returns><see langword="true" /> when an APNG animation chunk is present.</returns>
    private static bool IsAnimatedPng(ReadOnlySpan<byte> content)
    {
        if (!content.StartsWith(_pngSignature))
            return false;

        var offset = _pngSignature.Length;

        while (offset + 12 <= content.Length)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(
                offset,
                4));
            var completeChunkLength = 12L + chunkLength;

            if (completeChunkLength > content.Length - offset)
                return false;

            if (content.Slice(
                    offset + 4,
                    4).SequenceEqual("acTL"u8))
                return true;

            offset += (int)completeChunkLength;
        }

        return false;
    }

    /// <summary>
    /// Detects WebP animation chunks without decoding pixels.
    /// </summary>
    /// <param name="content">The untrusted encoded bytes.</param>
    /// <returns><see langword="true" /> when a WebP animation chunk is present.</returns>
    private static bool IsAnimatedWebp(ReadOnlySpan<byte> content)
    {
        if (content.Length < 12 ||
            !content[..4].SequenceEqual("RIFF"u8) ||
            !content.Slice(
                8,
                4).SequenceEqual("WEBP"u8))
        {
            return false;
        }

        var offset = 12;

        while (offset + 8 <= content.Length)
        {
            var chunkLength = BinaryPrimitives.ReadUInt32LittleEndian(content.Slice(
                offset + 4,
                4));
            var paddedChunkLength = chunkLength + (chunkLength & 1);
            var completeChunkLength = 8L + paddedChunkLength;

            if (completeChunkLength > content.Length - offset)
                return false;

            var chunkType = content.Slice(
                offset,
                4);

            if (chunkType.SequenceEqual("ANIM"u8) ||
                chunkType.SequenceEqual("ANMF"u8))
            {
                return true;
            }

            offset += (int)completeChunkLength;
        }

        return false;
    }

    /// <summary>
    /// Determines whether an encoded orientation swaps width and height.
    /// </summary>
    /// <param name="origin">The encoded orientation.</param>
    /// <returns><see langword="true" /> when the oriented edges are swapped.</returns>
    private static bool SwapsEdges(SKEncodedOrigin origin)
    {
        return origin is SKEncodedOrigin.LeftTop or
            SKEncodedOrigin.RightTop or
            SKEncodedOrigin.RightBottom or
            SKEncodedOrigin.LeftBottom;
    }

    /// <summary>
    /// Creates the transform that applies orientation correction and output scaling.
    /// </summary>
    /// <param name="origin">The encoded orientation.</param>
    /// <param name="sourceWidth">The decoded source width.</param>
    /// <param name="sourceHeight">The decoded source height.</param>
    /// <param name="outputWidth">The normalized output width.</param>
    /// <param name="outputHeight">The normalized output height.</param>
    /// <returns>The orientation and scaling matrix.</returns>
    private static SKMatrix CreateOrientationMatrix(
        SKEncodedOrigin origin,
        int sourceWidth,
        int sourceHeight,
        int outputWidth,
        int outputHeight)
    {
        var orientedWidth = SwapsEdges(origin) ? sourceHeight : sourceWidth;
        var orientedHeight = SwapsEdges(origin) ? sourceWidth : sourceHeight;
        var scaleX = (float)outputWidth / orientedWidth;
        var scaleY = (float)outputHeight / orientedHeight;
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => CreateMatrix(
                -1,
                0,
                sourceWidth,
                0,
                1,
                0),
            SKEncodedOrigin.BottomRight => CreateMatrix(
                -1,
                0,
                sourceWidth,
                0,
                -1,
                sourceHeight),
            SKEncodedOrigin.BottomLeft => CreateMatrix(
                1,
                0,
                0,
                0,
                -1,
                sourceHeight),
            SKEncodedOrigin.LeftTop => CreateMatrix(
                0,
                1,
                0,
                1,
                0,
                0),
            SKEncodedOrigin.RightTop => CreateMatrix(
                0,
                -1,
                sourceHeight,
                1,
                0,
                0),
            SKEncodedOrigin.RightBottom => CreateMatrix(
                0,
                -1,
                sourceHeight,
                -1,
                0,
                sourceWidth),
            SKEncodedOrigin.LeftBottom => CreateMatrix(
                0,
                1,
                0,
                -1,
                0,
                sourceWidth),
            _ => SKMatrix.CreateIdentity()
        };

        matrix.ScaleX *= scaleX;
        matrix.SkewX *= scaleX;
        matrix.TransX *= scaleX;
        matrix.SkewY *= scaleY;
        matrix.ScaleY *= scaleY;
        matrix.TransY *= scaleY;

        return matrix;
    }

    /// <summary>
    /// Creates an affine image transform matrix.
    /// </summary>
    /// <param name="scaleX">The horizontal scale.</param>
    /// <param name="skewX">The horizontal skew.</param>
    /// <param name="transX">The horizontal translation.</param>
    /// <param name="skewY">The vertical skew.</param>
    /// <param name="scaleY">The vertical scale.</param>
    /// <param name="transY">The vertical translation.</param>
    /// <returns>The affine transform matrix.</returns>
    private static SKMatrix CreateMatrix(
        float scaleX,
        float skewX,
        float transX,
        float skewY,
        float scaleY,
        float transY)
    {
        return new SKMatrix(
            scaleX,
            skewX,
            transX,
            skewY,
            scaleY,
            transY,
            0,
            0,
            1);
    }
}
