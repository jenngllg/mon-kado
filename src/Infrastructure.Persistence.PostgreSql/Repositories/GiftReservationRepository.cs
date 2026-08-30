using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL persistence operations for gift reservations.
/// </summary>
/// <param name="context">The database context.</param>
public class GiftReservationRepository(MonKadoDbContext context) : IGiftReservationRepository
{
    /// <inheritdoc />
    public void Add(GiftReservation reservation)
    {
        context.GiftReservations.Add(reservation);
    }

    /// <inheritdoc />
    public void Remove(GiftReservation reservation)
    {
        context.GiftReservations.Remove(reservation);
    }

    /// <inheritdoc />
    public Task<GiftReservation?> GetAsync(
        Guid wishlistId,
        Guid wishId,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        return context.GiftReservations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                reservation => reservation.WishlistId == wishlistId &&
                    reservation.WishId == wishId &&
                    reservation.WishlistParticipantId == participantId,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<GiftReservation?> GetForUpdateAsync(
        Guid wishlistId,
        Guid wishId,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        return context.GiftReservations
            .FromSqlInterpolated($"""
                SELECT reservation.*, reservation.xmin
                FROM public.gift_reservations AS reservation
                WHERE reservation.wishlist_id = {wishlistId}
                    AND reservation.wish_id = {wishId}
                    AND reservation.wishlist_participant_id = {participantId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<GiftReservation>> GetByParticipantForUpdateAsync(
        Guid participantId,
        CancellationToken cancellationToken)
    {
        return await context.GiftReservations
            .FromSqlInterpolated($"""
                SELECT reservation.*, reservation.xmin
                FROM public.gift_reservations AS reservation
                WHERE reservation.wishlist_participant_id = {participantId}
                ORDER BY reservation.wish_id
                FOR UPDATE
                """)
            .ToArrayAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> GetQuantitiesAsync(
        Guid wishlistId,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        return await context.GiftReservations
            .AsNoTracking()
            .Where(reservation => reservation.WishlistId == wishlistId &&
                reservation.WishlistParticipantId == participantId)
            .ToDictionaryAsync(
                reservation => reservation.WishId,
                reservation => reservation.Quantity,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> GetTotalQuantityAsync(
        Guid wishId,
        CancellationToken cancellationToken)
    {
        return context.GiftReservations
            .Where(reservation => reservation.WishId == wishId)
            .SumAsync(
                reservation => reservation.Quantity,
                cancellationToken);
    }
}
