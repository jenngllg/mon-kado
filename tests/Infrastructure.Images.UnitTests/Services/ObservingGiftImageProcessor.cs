using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Services;

using SkiaSharp;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.UnitTests.Services;

/// <summary>Observes native ownership through the existing decoder and encoder boundaries.</summary>
public class ObservingGiftImageProcessor : GiftImageProcessor
{
    private SKData? _data;
    private SKCodec? _codec;
    private SKBitmap? _source;
    private SKImage? _normalized;

    public SKImageInfo DecodedInfo
    {
        get; private set;
    }
    public bool ReusedValidatedCodec
    {
        get; private set;
    }
    public bool ReleasedSourceBeforeEncoding
    {
        get; private set;
    }
    public bool SourcePixelsSharedBeforeDrawing
    {
        get; private set;
    }
    public bool SourceResourcesDisposed =>
        _data?.Handle == IntPtr.Zero && _codec?.Handle == IntPtr.Zero && _source?.Handle == IntPtr.Zero;
    public bool NormalizedImageDisposed => _normalized?.Handle == IntPtr.Zero;
    public Action? AfterDecode
    {
        get; init;
    }
    public bool FailEncoding
    {
        get; init;
    }

    /// <inheritdoc />
    protected override SKCodec? CreateCodec(SKData data)
    {
        _data = data;
        _codec = base.CreateCodec(data);

        return _codec;
    }

    /// <inheritdoc />
    protected override SKBitmap? Decode(
        SKCodec codec,
        SKImageInfo imageInfo)
    {
        ReusedValidatedCodec = ReferenceEquals(
            _codec,
            codec);
        DecodedInfo = imageInfo;
        _source = base.Decode(
            codec,
            imageInfo);
        AfterDecode?.Invoke();

        return _source;
    }

    /// <inheritdoc />
    protected override SKData? Encode(SKImage image)
    {
        _normalized = image;
        ReleasedSourceBeforeEncoding = SourceResourcesDisposed;

        return FailEncoding ? null : base.Encode(image);
    }

    /// <inheritdoc />
    protected override SKSurface? CreateSurface(SKImageInfo imageInfo)
    {
        var source = Assert.IsType<SKBitmap>(_source);
        using var image = SKImage.FromBitmap(source);
        using var pixels = image.PeekPixels();
        SourcePixelsSharedBeforeDrawing = source.IsImmutable && source.GetPixels() == pixels.GetPixels();

        return base.CreateSurface(imageInfo);
    }
}
