using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains editable suggestions without creating a gift wish.</summary>
[ExcludeFromCodeCoverage]
public class WishImportPreview
{
    /// <summary>Gets the suggested gift name.</summary>
    public string? Name
    {
        get; init;
    }
    /// <summary>Gets the original product URL.</summary>
    public string? Url
    {
        get; init;
    }
    /// <summary>Gets the unambiguous price in euros.</summary>
    public decimal? Price
    {
        get; init;
    }
    /// <summary>Gets the initial requested quantity.</summary>
    public int Quantity
    {
        get; init;
    }
    /// <summary>Gets the normalized image held only in client memory.</summary>
    public WishImportImage? Image
    {
        get; init;
    }
    /// <summary>Gets stable warning codes for incomplete extraction.</summary>
    public IEnumerable<string> Warnings { get; init; } = [];
}
