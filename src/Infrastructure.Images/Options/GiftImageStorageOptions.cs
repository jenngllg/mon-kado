using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Images.Options;

/// <summary>
/// Defines durable local gift-image storage settings.
/// </summary>
[ExcludeFromCodeCoverage]
public class GiftImageStorageOptions
{
    /// <summary>
    /// Gets the configuration section name.
    /// </summary>
    public const string SectionName = "GiftImages";

    /// <summary>
    /// Gets the shared image storage root path.
    /// </summary>
    public string? StoragePath
    {
        get; init;
    }
}
