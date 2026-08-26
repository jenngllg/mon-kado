using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Options;

/// <summary>
/// Configures owner-facing wishlist share URLs.
/// </summary>
[ExcludeFromCodeCoverage]
public class WishlistSharingOptions
{
    /// <summary>Identifies the configuration section.</summary>
    public const string SectionName = "WishlistSharing";

    /// <summary>Gets the exact frontend origin used for share URLs.</summary>
    public string? FrontendOrigin
    {
        get; init;
    }
}
