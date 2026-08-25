using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>
/// Records private wishlist service calls for functional tests.
/// </summary>
public class RecordingWishlistService : IWishlistService
{
    private static readonly DateTime _createdAt = new(
        2026,
        8,
        24,
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
        string Name,
        string NormalizedName,
        WishlistOccasion Occasion,
        DateOnly? EventDate,
        string? Message)> Creations
    {
        get;
    } = [];

    /// <summary>
    /// Gets the recorded update calls.
    /// </summary>
    public List<(
        Guid OwnerId,
        Guid WishlistId,
        string Name,
        string NormalizedName,
        WishlistOccasion Occasion,
        DateOnly? EventDate,
        string? Message,
        uint ExpectedVersion)> Updates
    {
        get;
    } = [];

    /// <summary>
    /// Gets the recorded access calls.
    /// </summary>
    public List<(Guid MemberId, Guid WishlistId)> Accesses { get; } = [];

    /// <summary>
    /// Gets the recorded retrieval identifiers.
    /// </summary>
    public List<Guid> Retrievals { get; } = [];

    /// <summary>
    /// Gets the recorded owner collection retrieval identifiers.
    /// </summary>
    public List<Guid> OwnerRetrievals { get; } = [];

    /// <summary>
    /// Gets the configured owned wishlist collection.
    /// </summary>
    public List<WishlistDetails> OwnedWishlists { get; } = [];

    /// <summary>
    /// Gets the wishlists returned by identifier.
    /// </summary>
    public Dictionary<Guid, WishlistDetails> Wishlists { get; } = [];

    /// <summary>
    /// Gets or sets the access result returned by the fake.
    /// </summary>
    public WishlistAccess Access
    {
        get; set;
    } = WishlistAccess.Owner;

    /// <summary>
    /// Gets or sets a value indicating whether creation finds the owner member.
    /// </summary>
    public bool MemberExists
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
    /// Gets or sets a value indicating whether wishlist update finds the resource.
    /// </summary>
    public bool WishlistExistsForUpdate
    {
        get; set;
    } = true;

    /// <summary>
    /// Gets or sets the version returned after a wishlist update.
    /// </summary>
    public uint UpdatedVersion
    {
        get; set;
    } = 43;

    /// <inheritdoc />
    public Task<WishlistDetails?> CreateAsync(
        Guid id,
        Guid ownerId,
        string name,
        string normalizedName,
        WishlistOccasion occasion,
        DateOnly? eventDate,
        string? message,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Creations.Add((
            id,
            ownerId,
            name,
            normalizedName,
            occasion,
            eventDate,
            message));

        if (Exception is not null)
            throw Exception;

        if (!MemberExists)
            return Task.FromResult<WishlistDetails?>(null);

        var wishlist = new WishlistDetails(
            id,
            name,
            occasion,
            eventDate,
            message,
            _createdAt,
            null,
            42);
        Wishlists[id] = wishlist;

        return Task.FromResult<WishlistDetails?>(wishlist);
    }

    /// <inheritdoc />
    public Task<WishlistDetails?> GetAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Retrievals.Add(wishlistId);

        if (Exception is not null)
            throw Exception;

        Wishlists.TryGetValue(
            wishlistId,
            out var wishlist);

        return Task.FromResult(wishlist);
    }

    /// <inheritdoc />
    public Task<WishlistDetails?> UpdateAsync(
        Guid ownerId,
        Guid wishlistId,
        string name,
        string normalizedName,
        WishlistOccasion occasion,
        DateOnly? eventDate,
        string? message,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Updates.Add((
            ownerId,
            wishlistId,
            name,
            normalizedName,
            occasion,
            eventDate,
            message,
            expectedVersion));

        if (Exception is not null)
            throw Exception;

        if (!WishlistExistsForUpdate)
            return Task.FromResult<WishlistDetails?>(null);

        var wishlist = new WishlistDetails(
            wishlistId,
            name,
            occasion,
            eventDate,
            message,
            _createdAt,
            _createdAt.AddDays(1),
            UpdatedVersion);
        Wishlists[wishlistId] = wishlist;

        return Task.FromResult<WishlistDetails?>(wishlist);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<WishlistDetails>?> GetByOwnerIdAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        OwnerRetrievals.Add(ownerId);

        if (Exception is not null)
            throw Exception;

        if (!MemberExists)
            return Task.FromResult<IReadOnlyCollection<WishlistDetails>?>(null);

        return Task.FromResult<IReadOnlyCollection<WishlistDetails>?>(OwnedWishlists);
    }

    /// <inheritdoc />
    public Task<WishlistAccess> GetAccessAsync(
        Guid memberId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Accesses.Add((
            memberId,
            wishlistId));

        if (Exception is not null)
            throw Exception;

        return Task.FromResult(Access);
    }
}
