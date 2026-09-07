using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains private administrative report review data.</summary>
[ExcludeFromCodeCoverage]
public class VersionedWishlistReportDetails
{
    /// <summary>The safe report representation.</summary>
    public WishlistReportDetails Report { get; init; } = new();
    /// <summary>The optimistic concurrency version.</summary>
    public uint Version
    {
        get; init;
    }
}
