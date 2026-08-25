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
        decimal? Price)> Creations
    {
        get;
    } = [];

    /// <summary>
    /// Gets the recorded retrieval identifiers.
    /// </summary>
    public List<(Guid WishlistId, Guid WishId)> Retrievals { get; } = [];

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

    /// <inheritdoc />
    public Task<WishDetails?> CreateAsync(
        Guid id,
        Guid ownerId,
        Guid wishlistId,
        string name,
        string? note,
        string? url,
        decimal? price,
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
            price));

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
            42);
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
}
