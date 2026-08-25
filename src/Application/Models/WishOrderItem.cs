using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents one gift wish in a reordered collection.
/// </summary>
[ExcludeFromCodeCoverage]
public class WishOrderItem(
    Guid id,
    long position,
    uint version)
{
    /// <summary>
    /// Gets the wish identifier.
    /// </summary>
    public Guid Id { get; } = id;

    /// <summary>
    /// Gets the position inside the parent wishlist.
    /// </summary>
    public long Position { get; } = position;

    /// <summary>
    /// Gets the individual wish concurrency version.
    /// </summary>
    public uint Version { get; } = version;
}
