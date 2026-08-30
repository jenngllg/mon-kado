using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Retrieves, replaces and cancels gift reservations for shared-wishlist participants.
/// </summary>
public interface IGiftReservationService
{
    /// <summary>Gets the current participant's reservation for one gift.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="participantId">The participant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The reservation when found.</returns>
    Task<GiftReservationDetails?> GetAsync(
        Guid wishlistId,
        Guid wishId,
        Guid participantId,
        CancellationToken cancellationToken);

    /// <summary>Gets the current participant's quantities keyed by gift wish.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="participantId">The participant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The reserved quantities keyed by gift-wish identifier.</returns>
    Task<IReadOnlyDictionary<Guid, int>> GetQuantitiesAsync(
        Guid wishlistId,
        Guid participantId,
        CancellationToken cancellationToken);

    /// <summary>Creates or replaces the current participant's reservation.</summary>
    /// <param name="request">The reservation mutation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current reservation and whether it was created.</returns>
    Task<GiftReservationMutationResult> UpsertAsync(
        GiftReservationMutationRequest request,
        CancellationToken cancellationToken);

    /// <summary>Cancels the current participant's reservation.</summary>
    /// <param name="request">The reservation cancellation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the reservation was deleted.</returns>
    Task<bool> CancelAsync(
        GiftReservationCancellationRequest request,
        CancellationToken cancellationToken);
}
