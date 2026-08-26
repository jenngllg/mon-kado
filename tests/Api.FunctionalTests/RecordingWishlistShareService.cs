using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>
/// Records wishlist share-service calls for functional tests.
/// </summary>
public class RecordingWishlistShareService : IWishlistShareService
{
    private static readonly DateTime _createdAt = new(
        2026,
        8,
        26,
        10,
        0,
        0,
        DateTimeKind.Utc);

    /// <summary>Gets recorded creation calls.</summary>
    public List<(Guid Id, Guid OwnerId, Guid WishlistId)> Creations { get; } = [];

    /// <summary>Gets recorded owner retrieval calls.</summary>
    public List<(Guid OwnerId, Guid WishlistId)> Retrievals { get; } = [];

    /// <summary>Gets recorded rotation calls.</summary>
    public List<(Guid OwnerId, Guid WishlistId, uint ExpectedVersion)> Rotations { get; } = [];

    /// <summary>Gets recorded deletion calls.</summary>
    public List<(Guid OwnerId, Guid WishlistId, uint ExpectedVersion)> Deletions { get; } = [];

    /// <summary>Gets recorded public retrieval calls.</summary>
    public List<(Guid ShareLinkId, string Secret)> PublicRetrievals { get; } = [];

    /// <summary>Gets active fake share links keyed by wishlist.</summary>
    public Dictionary<Guid, WishlistShareLinkDetails> ShareLinks { get; } = [];

    /// <summary>Gets or sets the exception thrown by the fake.</summary>
    public Exception? Exception
    {
        get; set;
    }

    /// <summary>Gets or sets the public wishlist returned by the fake.</summary>
    public SharedWishlistDetails? SharedWishlist
    {
        get; set;
    } = new(
        Guid.Parse("0198e75d-8280-7000-8000-000000000001"),
        "Jenn",
        "Anniversaire",
        WishlistOccasion.Birthday,
        new DateOnly(
            2026,
            9,
            23),
        "Merci",
        [
            new SharedWishDetails(
                Guid.Parse("0198e75d-8280-7000-8000-000000000002"),
                "Livre",
                "https://example.com/book",
                19.99m)
        ]);

    /// <inheritdoc />
    public Task<WishlistShareLinkDetails?> CreateAsync(
        Guid id,
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Creations.Add((
            id,
            ownerId,
            wishlistId));
        ThrowIfConfigured();
        var details = CreateDetails(
            id,
            wishlistId,
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            42,
            null);
        ShareLinks[wishlistId] = details;

        return Task.FromResult<WishlistShareLinkDetails?>(details);
    }

    /// <inheritdoc />
    public Task<WishlistShareLinkDetails?> GetAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Retrievals.Add((
            ownerId,
            wishlistId));
        ThrowIfConfigured();
        ShareLinks.TryGetValue(
            wishlistId,
            out var details);

        return Task.FromResult(details);
    }

    /// <inheritdoc />
    public Task<WishlistShareLinkDetails?> RotateAsync(
        Guid ownerId,
        Guid wishlistId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Rotations.Add((
            ownerId,
            wishlistId,
            expectedVersion));
        ThrowIfConfigured();

        if (!ShareLinks.TryGetValue(
                wishlistId,
                out var current))
            return Task.FromResult<WishlistShareLinkDetails?>(null);

        var details = CreateDetails(
            current.Id,
            wishlistId,
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB",
            43,
            _createdAt.AddMinutes(1));
        ShareLinks[wishlistId] = details;

        return Task.FromResult<WishlistShareLinkDetails?>(details);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(
        Guid ownerId,
        Guid wishlistId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Deletions.Add((
            ownerId,
            wishlistId,
            expectedVersion));
        ThrowIfConfigured();

        return Task.FromResult(ShareLinks.Remove(wishlistId));
    }

    /// <inheritdoc />
    public Task<SharedWishlistDetails?> GetSharedAsync(
        Guid shareLinkId,
        string secret,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PublicRetrievals.Add((
            shareLinkId,
            secret));
        ThrowIfConfigured();

        return Task.FromResult(SharedWishlist);
    }

    /// <summary>
    /// Creates deterministic owner-facing share-link details.
    /// </summary>
    /// <param name="id">The share-link identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="secret">The fake secret.</param>
    /// <param name="version">The fake version.</param>
    /// <param name="updatedAt">The optional update time.</param>
    /// <returns>The deterministic details.</returns>
    private static WishlistShareLinkDetails CreateDetails(
        Guid id,
        Guid wishlistId,
        string secret,
        uint version,
        DateTime? updatedAt)
    {
        return new WishlistShareLinkDetails(
            id,
            wishlistId,
            secret,
            _createdAt,
            updatedAt,
            version);
    }

    /// <summary>
    /// Throws the configured functional-test exception.
    /// </summary>
    private void ThrowIfConfigured()
    {
        if (Exception is not null)
            throw Exception;
    }
}
