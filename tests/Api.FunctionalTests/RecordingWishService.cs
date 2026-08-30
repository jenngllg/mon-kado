using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>
/// Records gift wish service calls for functional tests.
/// </summary>
public class RecordingWishService : IWishService
{
    private static readonly DateTime _createdAt = new(
        2026,
        8,
        25,
        12,
        0,
        0,
        DateTimeKind.Utc);

    /// <summary>
    /// Gets the recorded creation calls.
    /// </summary>
    public List<(
        Guid Id,
        Guid OwnerId,
        Guid WishlistId,
        string Name,
        string? Note,
        string? Url,
        decimal? Price,
        int Quantity)> Creations
    {
        get;
    } = [];

    /// <summary>
    /// Gets the recorded retrieval identifiers.
    /// </summary>
    public List<(Guid WishlistId, Guid WishId)> Retrievals { get; } = [];

    /// <summary>
    /// Gets the recorded collection retrieval calls.
    /// </summary>
    public List<(Guid OwnerId, Guid WishlistId)> CollectionRetrievals { get; } = [];

    /// <summary>
    /// Gets the recorded reorder calls.
    /// </summary>
    public List<(
        Guid OwnerId,
        Guid WishlistId,
        IReadOnlyCollection<Guid> WishIds,
        uint ExpectedVersion)> Reorders
    {
        get;
    } = [];

    /// <summary>
    /// Gets the recorded update calls.
    /// </summary>
    public List<(
        Guid OwnerId,
        Guid WishlistId,
        Guid WishId,
        string Name,
        string? Note,
        string? Url,
        decimal? Price,
        int Quantity,
        uint ExpectedVersion)> Updates
    {
        get;
    } = [];

    /// <summary>
    /// Gets the recorded deletion calls.
    /// </summary>
    public List<(
        Guid OwnerId,
        Guid WishlistId,
        Guid WishId,
        uint ExpectedVersion)> Deletions
    {
        get;
    } = [];

    /// <summary>
    /// Gets the gift wishes returned by their nested identifiers.
    /// </summary>
    public Dictionary<(Guid WishlistId, Guid WishId), WishDetails> Wishes { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the parent wishlist exists during creation.
    /// </summary>
    public bool WishlistExists
    {
        get; set;
    } = true;

    /// <summary>
    /// Gets or sets the exception thrown by the fake.
    /// </summary>
    public Exception? Exception
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the collection version returned by the fake.
    /// </summary>
    public uint CollectionVersion
    {
        get; set;
    } = 84;

    /// <inheritdoc />
    public Task<WishCollectionDetails> GetCollectionAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CollectionRetrievals.Add((
            ownerId,
            wishlistId));

        if (Exception is not null)
            throw Exception;

        var wishes = Wishes.Values
            .Where(wish => wish.WishlistId == wishlistId)
            .OrderBy(wish => wish.Position)
            .ToArray();

        return Task.FromResult(new WishCollectionDetails(
            wishes,
            CollectionVersion));
    }

    /// <inheritdoc />
    public Task<WishOrderDetails> ReorderAsync(
        Guid ownerId,
        Guid wishlistId,
        IReadOnlyCollection<Guid> wishIds,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Reorders.Add((
            ownerId,
            wishlistId,
            wishIds,
            expectedVersion));

        if (Exception is not null)
            throw Exception;

        var positions = Wishes.Values
            .Where(wish => wish.WishlistId == wishlistId)
            .Select(wish => wish.Position)
            .Order()
            .ToArray();
        var items = wishIds
            .Select((
                wishId,
                index) => new WishOrderItem(
                    wishId,
                    positions[index],
                    100u + (uint)index))
            .ToArray();
        CollectionVersion++;

        return Task.FromResult(new WishOrderDetails(
            items,
            CollectionVersion));
    }

    /// <inheritdoc />
    public Task<WishDetails?> CreateAsync(
        Guid id,
        Guid ownerId,
        Guid wishlistId,
        string name,
        string? note,
        string? url,
        decimal? price,
        int quantity,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Creations.Add((
            id,
            ownerId,
            wishlistId,
            name,
            note,
            url,
            price,
            quantity));

        if (Exception is not null)
            throw Exception;

        if (!WishlistExists)
            return Task.FromResult<WishDetails?>(null);

        var wish = new WishDetails(
            id,
            wishlistId,
            name,
            note,
            url,
            price,
            1,
            _createdAt,
            null,
            42,
            quantity);
        Wishes[(wishlistId, id)] = wish;

        return Task.FromResult<WishDetails?>(wish);
    }

    /// <inheritdoc />
    public Task<WishDetails?> GetAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Retrievals.Add((
            wishlistId,
            wishId));

        if (Exception is not null)
            throw Exception;

        Wishes.TryGetValue(
            (wishlistId, wishId),
            out var wish);

        return Task.FromResult(wish);
    }

    /// <inheritdoc />
    public Task<WishDetails?> UpdateAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        string name,
        string? note,
        string? url,
        decimal? price,
        int quantity,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Updates.Add((
            ownerId,
            wishlistId,
            wishId,
            name,
            note,
            url,
            price,
            quantity,
            expectedVersion));

        if (Exception is not null)
            throw Exception;

        if (!WishlistExists ||
            !Wishes.TryGetValue(
                (wishlistId, wishId),
                out var currentWish))
        {
            return Task.FromResult<WishDetails?>(null);
        }

        var wish = new WishDetails(
            wishId,
            wishlistId,
            name,
            note,
            url,
            price,
            currentWish.Position,
            currentWish.CreatedAt,
            _createdAt.AddHours(1),
            expectedVersion + 1,
            quantity);
        Wishes[(wishlistId, wishId)] = wish;

        return Task.FromResult<WishDetails?>(wish);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Deletions.Add((
            ownerId,
            wishlistId,
            wishId,
            expectedVersion));

        if (Exception is not null)
            throw Exception;

        if (!WishlistExists)
            return Task.FromResult(false);

        return Task.FromResult(Wishes.Remove((wishlistId, wishId)));
    }
}
