using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;

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

    /// <summary>Adds a member reservation history entry to the current unit of work.</summary>
    /// <param name="history">The history entry to add.</param>
    void AddHistory(GiftReservationHistory history);

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
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The total reserved quantity.</returns>
    Task<int> GetTotalQuantityAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken);

    /// <summary>Gets and tracks one reservation history entry.</summary>
    /// <param name="reservationId">The reservation lifecycle identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The history entry when found.</returns>
    Task<GiftReservationHistory?> GetHistoryForUpdateAsync(
        Guid reservationId,
        CancellationToken cancellationToken);

    /// <summary>Gets the current labels of a reservation source.</summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="wishId">The gift-wish identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The source labels when the gift still exists.</returns>
    Task<GiftReservationHistorySource?> GetHistorySourceAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken);

    /// <summary>Determines whether a member exists.</summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the member exists.</returns>
    Task<bool> MemberExistsAsync(
        Guid memberId,
        CancellationToken cancellationToken);

    /// <summary>Counts member reservation history entries matching an optional status.</summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="status">The optional lifecycle status.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of matching history entries.</returns>
    Task<int> CountHistoryAsync(
        Guid memberId,
        GiftReservationHistoryStatus? status,
        CancellationToken cancellationToken);

    /// <summary>Gets one ordered page of member reservation history.</summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="status">The optional lifecycle status.</param>
    /// <param name="offset">The number of matching entries to skip.</param>
    /// <param name="pageSize">The maximum number of entries to return.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching history page ordered by latest activity.</returns>
    Task<IReadOnlyCollection<GiftReservationHistoryDetails>> GetHistoryPageAsync(
        Guid memberId,
        GiftReservationHistoryStatus? status,
        int offset,
        int pageSize,
        CancellationToken cancellationToken);
}
