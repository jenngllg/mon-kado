using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

/// <summary>
/// Records gift-reservation service calls for functional tests.
/// </summary>
public class RecordingGiftReservationService : IGiftReservationService
{
    private static readonly DateTime _createdAt = new(
        2026,
        8,
        30,
        10,
        0,
        0,
        DateTimeKind.Utc);

    /// <summary>Gets recorded reservation retrievals.</summary>
    public List<(Guid WishlistId, Guid WishId, Guid ParticipantId)> Retrievals { get; } = [];

    /// <summary>Gets recorded participant-quantity retrievals.</summary>
    public List<(Guid WishlistId, Guid ParticipantId)> QuantityRetrievals { get; } = [];

    /// <summary>Gets recorded reservation mutations.</summary>
    public List<GiftReservationMutationRequest> Mutations { get; } = [];

    /// <summary>Gets or sets the current reservation returned by retrieval.</summary>
    public GiftReservationDetails? Reservation
    {
        get; set;
    }

    /// <summary>Gets or sets whether the next mutation creates a reservation.</summary>
    public bool IsCreated { get; set; } = true;

    /// <summary>Gets or sets an exception thrown by reservation operations.</summary>
    public Exception? Exception
    {
        get; set;
    }

    /// <summary>Gets quantities returned for shared-wishlist display.</summary>
    public Dictionary<Guid, int> Quantities { get; } = [];

    /// <inheritdoc />
    public Task<GiftReservationDetails?> GetAsync(
        Guid wishlistId,
        Guid wishId,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Retrievals.Add((
            wishlistId,
            wishId,
            participantId));

        if (Exception is not null)
            throw Exception;

        return Task.FromResult(Reservation);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Guid, int>> GetQuantitiesAsync(
        Guid wishlistId,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        QuantityRetrievals.Add((
            wishlistId,
            participantId));

        if (Exception is not null)
            throw Exception;

        return Task.FromResult<IReadOnlyDictionary<Guid, int>>(Quantities);
    }

    /// <inheritdoc />
    public Task<GiftReservationMutationResult> UpsertAsync(
        GiftReservationMutationRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Mutations.Add(request);

        if (Exception is not null)
            throw Exception;

        var reservation = Reservation ?? new GiftReservationDetails
        {
            Id = request.ReservationId,
            WishId = request.WishId,
            Quantity = request.Quantity,
            CreatedAt = _createdAt,
            Version = 42
        };

        return Task.FromResult(new GiftReservationMutationResult
        {
            Reservation = reservation,
            IsCreated = IsCreated
        });
    }
}
