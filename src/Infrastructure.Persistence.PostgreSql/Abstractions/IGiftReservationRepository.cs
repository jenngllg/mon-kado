using JennGllg.Fr.MonKado.Back.Domain.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines PostgreSQL persistence operations for gift reservations.
/// </summary>
public interface IGiftReservationRepository
{
    /// <summary>Adds a reservation to the current unit of work.</summary>
    /// <param name="reservation">The reservation to add.</param>
    void Add(GiftReservation reservation);

    /// <summary>Removes a reservation from the current unit of work.</summary>
    /// <param name="reservation">The reservation to remove.</param>
    void Remove(GiftReservation reservation);

    /// <summary>Gets a reservation without tracking it.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="participantId">The participant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The reservation when found.</returns>
    Task<GiftReservation?> GetAsync(
        Guid wishlistId,
        Guid wishId,
        Guid participantId,
        CancellationToken cancellationToken);

    /// <summary>Gets and locks a reservation for replacement.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="participantId">The participant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The locked reservation when found.</returns>
    Task<GiftReservation?> GetForUpdateAsync(
        Guid wishlistId,
        Guid wishId,
        Guid participantId,
        CancellationToken cancellationToken);

    /// <summary>Gets and locks every reservation of one participant.</summary>
    /// <param name="participantId">The participant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The locked reservations.</returns>
    Task<IReadOnlyCollection<GiftReservation>> GetByParticipantForUpdateAsync(
        Guid participantId,
        CancellationToken cancellationToken);

    /// <summary>Gets the quantities reserved by one participant, keyed by wish.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="participantId">The participant identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The reserved quantities keyed by gift-wish identifier.</returns>
    Task<IReadOnlyDictionary<Guid, int>> GetQuantitiesAsync(
        Guid wishlistId,
        Guid participantId,
        CancellationToken cancellationToken);

    /// <summary>Gets the total quantity reserved for one gift.</summary>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The total reserved quantity.</returns>
    Task<int> GetTotalQuantityAsync(
        Guid wishId,
        CancellationToken cancellationToken);
}
