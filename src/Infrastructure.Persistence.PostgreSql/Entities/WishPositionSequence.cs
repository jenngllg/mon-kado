using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>
/// Represents the position allocator and collection version of one wishlist.
/// </summary>
[ExcludeFromCodeCoverage]
public class WishPositionSequence
{
    private WishPositionSequence()
    {
    }

    /// <summary>
    /// Initializes a collection state snapshot.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="nextPosition">The last allocated position.</param>
    /// <param name="currentCount">The current number of gift wishes.</param>
    /// <param name="version">The PostgreSQL collection version.</param>
    public WishPositionSequence(
        Guid wishlistId,
        long nextPosition,
        int currentCount,
        uint version)
    {
        WishlistId = wishlistId;
        NextPosition = nextPosition;
        CurrentCount = currentCount;
        Version = version;
    }

    /// <summary>
    /// Gets the parent wishlist identifier.
    /// </summary>
    public Guid WishlistId
    {
        get; private set;
    }

    /// <summary>
    /// Gets the last allocated position.
    /// </summary>
    public long NextPosition
    {
        get; private set;
    }

    /// <summary>
    /// Gets the current number of gift wishes.
    /// </summary>
    public int CurrentCount
    {
        get; private set;
    }

    /// <summary>
    /// Gets the PostgreSQL collection concurrency version.
    /// </summary>
    public uint Version
    {
        get; private set;
    }
}
