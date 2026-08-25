using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents the complete order of a versioned gift wish collection.
/// </summary>
[ExcludeFromCodeCoverage]
public class WishOrderDetails(
    IReadOnlyCollection<WishOrderItem> wishes,
    uint version)
{
    /// <summary>
    /// Gets the complete ordered collection.
    /// </summary>
    public IReadOnlyCollection<WishOrderItem> Wishes { get; } = wishes;

    /// <summary>
    /// Gets the collection concurrency version.
    /// </summary>
    public uint Version { get; } = version;
}
