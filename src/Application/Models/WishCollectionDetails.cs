using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents a versioned collection of gift wishes.
/// </summary>
[ExcludeFromCodeCoverage]
public class WishCollectionDetails(
    IReadOnlyCollection<WishDetails> wishes,
    uint version)
{
    /// <summary>
    /// Gets the ordered gift wishes.
    /// </summary>
    public IReadOnlyCollection<WishDetails> Wishes { get; } = wishes;

    /// <summary>
    /// Gets the collection concurrency version.
    /// </summary>
    public uint Version { get; } = version;
}
