using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Identifies a current image selected for a public social preview.</summary>
[ExcludeFromCodeCoverage]
public class WishlistSharePreviewImage
{
    /// <summary>Gets the parent wish identifier.</summary>
    public Guid WishId
    {
        get; init;
    }

    /// <summary>Gets the immutable image identifier.</summary>
    public Guid ImageId
    {
        get; init;
    }
}
