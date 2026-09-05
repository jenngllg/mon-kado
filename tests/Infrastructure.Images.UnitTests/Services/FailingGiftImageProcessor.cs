using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Services;

using SkiaSharp;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.UnitTests.Services;

/// <summary>
/// Simulates native SkiaSharp failures through the processor's public behavior.
/// </summary>
/// <param name="failureStage">The native operation that must fail.</param>
public class FailingGiftImageProcessor(string failureStage) : GiftImageProcessor
{
    /// <inheritdoc />
    protected override SKCodec? CreateCodec(SKData data)
    {
        return failureStage == "codec"
            ? null
            : base.CreateCodec(data);
    }

    /// <inheritdoc />
    protected override SKBitmap? Decode(SKData data)
    {
        return failureStage == "decode"
            ? null
            : base.Decode(data);
    }

    /// <inheritdoc />
    protected override SKSurface? CreateSurface(SKImageInfo imageInfo)
    {
        return failureStage == "surface"
            ? null
            : base.CreateSurface(imageInfo);
    }

    /// <inheritdoc />
    protected override SKData? Encode(SKImage image)
    {
        return failureStage == "encode"
            ? null
            : base.Encode(image);
    }
}
