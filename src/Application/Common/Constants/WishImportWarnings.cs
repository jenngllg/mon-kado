using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Common.Constants;

/// <summary>Defines stable warnings for best-effort merchant extraction.</summary>
[ExcludeFromCodeCoverage]
public static class WishImportWarnings
{
    /// <summary>The merchant document could not be retrieved.</summary>
    public const string PageUnavailable = "WISH_IMPORT_PAGE_UNAVAILABLE";
    /// <summary>No valid gift name could be extracted.</summary>
    public const string NameUnavailable = "WISH_IMPORT_NAME_UNAVAILABLE";
    /// <summary>No unambiguous euro price could be extracted.</summary>
    public const string PriceUnavailable = "WISH_IMPORT_PRICE_UNAVAILABLE";
    /// <summary>The merchant price uses an unsupported or unspecified currency.</summary>
    public const string CurrencyUnsupported = "WISH_IMPORT_CURRENCY_UNSUPPORTED";
    /// <summary>No safe usable image could be extracted.</summary>
    public const string ImageUnavailable = "WISH_IMPORT_IMAGE_UNAVAILABLE";
}
