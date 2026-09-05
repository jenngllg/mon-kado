using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;

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
    public void AddHistory(GiftReservationHistory history)
    {
        context.GiftReservationHistories.Add(history);
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

    /// <inheritdoc />
    public Task<GiftReservationHistory?> GetHistoryForUpdateAsync(
        Guid reservationId,
        CancellationToken cancellationToken)
    {
        return context.GiftReservationHistories
            .SingleOrDefaultAsync(
                history => history.Id == reservationId,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<GiftReservationHistorySource?> GetHistorySourceAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        return context.Wishes
            .AsNoTracking()
            .Where(wish => wish.WishlistId == wishlistId && wish.Id == wishId)
            .Select(wish => new GiftReservationHistorySource(
                context.Wishlists
                    .Where(wishlist => wishlist.Id == wishlistId)
                    .Select(wishlist => wishlist.Name)
                    .Single(),
                wish.Name))
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> MemberExistsAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        return context.Users.AnyAsync(
            member => member.Id == memberId,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountHistoryAsync(
        Guid memberId,
        GiftReservationHistoryStatus? status,
        CancellationToken cancellationToken)
    {
        return CreateHistoryQuery(
                memberId,
                status)
            .CountAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<GiftReservationHistoryDetails>> GetHistoryPageAsync(
        Guid memberId,
        GiftReservationHistoryStatus? status,
        int offset,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return await CreateHistoryQuery(
                memberId,
                status)
            .OrderByDescending(history => history.LastActivityAt)
            .ThenByDescending(history => history.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(history => new GiftReservationHistoryDetails
            {
                Id = history.Id,
                WishlistId = history.WishlistId,
                WishlistName = context.Wishlists
                    .Where(wishlist => wishlist.Id == history.WishlistId)
                    .Select(wishlist => wishlist.Name)
                    .FirstOrDefault() ?? history.WishlistName,
                WishId = history.WishId,
                WishName = context.Wishes
                    .Where(wish => wish.Id == history.WishId && wish.WishlistId == history.WishlistId)
                    .Select(wish => wish.Name)
                    .FirstOrDefault() ?? history.WishName,
                ShareLinkId = context.WishlistShareLinks
                    .Where(shareLink => shareLink.WishlistId == history.WishlistId)
                    .Select(shareLink => (Guid?)shareLink.Id)
                    .FirstOrDefault(),
                Quantity = history.Quantity,
                Status = history.Status,
                CreatedAt = history.CreatedAt,
                LastActivityAt = history.LastActivityAt,
                EndedAt = history.EndedAt
            })
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Creates the common no-tracking query for a member's reservation history.
    /// </summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="status">The optional lifecycle status.</param>
    /// <returns>The filtered history query.</returns>
    private IQueryable<GiftReservationHistory> CreateHistoryQuery(
        Guid memberId,
        GiftReservationHistoryStatus? status)
    {
        var query = context.GiftReservationHistories
            .AsNoTracking()
            .Where(history => history.MemberId == memberId);

        if (status is not null)
            query = query.Where(history => history.Status == status);

        return query;
    }
}
