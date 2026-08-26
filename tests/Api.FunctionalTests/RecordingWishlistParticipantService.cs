using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>
/// Records wishlist participant calls for functional tests.
/// </summary>
public class RecordingWishlistParticipantService : IWishlistParticipantService
{
    private static readonly DateTime _expiresAt = new(
        2027,
        2,
        22,
        12,
        0,
        0,
        DateTimeKind.Utc);

    /// <summary>Gets recorded join calls.</summary>
    public List<(Guid WishlistId, Guid? MemberId, string? GuestToken, string? DisplayName)> Joins { get; } = [];

    /// <summary>Gets recorded current-participant calls.</summary>
    public List<(Guid WishlistId, Guid? MemberId, string? GuestToken)> Retrievals { get; } = [];

    /// <summary>Gets or sets the join result.</summary>
    public WishlistParticipantJoinResult JoinResult
    {
        get; set;
    } = new(
        new WishlistParticipantDetails(
            Guid.Parse("0198e75d-8280-7000-8000-000000000010"),
            "Guest Jenn"),
        true,
        "0198e75d828070008000000000000011.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
        _expiresAt);

    /// <summary>Gets or sets the lookup result.</summary>
    public WishlistParticipantLookupResult LookupResult
    {
        get; set;
    } = new(
        WishlistParticipantLookupOutcome.MissingIdentity,
        null);

    /// <summary>Gets or sets an exception thrown by joins.</summary>
    public Exception? JoinException
    {
        get; set;
    }

    /// <summary>Gets or sets an exception thrown by retrievals.</summary>
    public Exception? LookupException
    {
        get; set;
    }

    /// <inheritdoc />
    public Task<WishlistParticipantJoinResult> JoinAsync(
        WishlistParticipantJoinRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Joins.Add((
            request.WishlistId,
            request.MemberId,
            request.GuestToken,
            request.DisplayName));

        if (JoinException is not null)
            throw JoinException;

        return Task.FromResult(JoinResult);
    }

    /// <inheritdoc />
    public Task<WishlistParticipantLookupResult> GetCurrentAsync(
        Guid wishlistId,
        Guid? memberId,
        string? guestToken,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Retrievals.Add((
            wishlistId,
            memberId,
            guestToken));

        if (LookupException is not null)
            throw LookupException;

        return Task.FromResult(LookupResult);
    }
}
