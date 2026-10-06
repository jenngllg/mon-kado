using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains only the explicitly public social-preview metadata.</summary>
[ExcludeFromCodeCoverage]
public class WishlistSharePreview
{
    /// <summary>Gets the parent wishlist identifier.</summary>
    public Guid WishlistId
    {
        get; init;
    }

    /// <summary>Gets the public list title.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gets the bounded image references in manual wish order.</summary>
    public IReadOnlyList<WishlistSharePreviewImage> Images { get; init; } = [];
}
