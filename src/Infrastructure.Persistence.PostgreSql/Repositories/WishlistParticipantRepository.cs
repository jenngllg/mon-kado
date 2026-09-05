using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>
/// Provides PostgreSQL persistence operations for wishlist participants.
/// </summary>
/// <param name="context">The database context.</param>
public class WishlistParticipantRepository(MonKadoDbContext context) : IWishlistParticipantRepository
{
    /// <inheritdoc />
    public void Add(WishlistParticipant participant)
    {
        context.WishlistParticipants.Add(participant);
    }

    /// <inheritdoc />
    public void Remove(WishlistParticipant participant)
    {
        context.WishlistParticipants.Remove(participant);
    }

    /// <inheritdoc />
    public Task<WishlistParticipant?> GetByIdAsync(
        Guid wishlistId,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        return context.WishlistParticipants
            .AsNoTracking()
            .SingleOrDefaultAsync(
                participant => participant.WishlistId == wishlistId &&
                    participant.Id == participantId,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<WishlistParticipant?> GetByGuestSessionAsync(
        Guid wishlistId,
        Guid guestSessionId,
        CancellationToken cancellationToken)
    {
        return context.WishlistParticipants
            .AsNoTracking()
            .SingleOrDefaultAsync(
                participant => participant.WishlistId == wishlistId &&
                    participant.GuestSessionId == guestSessionId,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<WishlistParticipant?> GetByMemberForUpdateAsync(
        Guid wishlistId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        return context.WishlistParticipants
            .FromSqlInterpolated($"""
                SELECT participant.*
                FROM public.wishlist_participants AS participant
                WHERE participant.wishlist_id = {wishlistId}
                    AND participant.member_id = {memberId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<WishlistParticipant?> GetByGuestSessionForUpdateAsync(
        Guid wishlistId,
        Guid guestSessionId,
        CancellationToken cancellationToken)
    {
        return context.WishlistParticipants
            .FromSqlInterpolated($"""
                SELECT participant.*
                FROM public.wishlist_participants AS participant
                WHERE participant.wishlist_id = {wishlistId}
                    AND participant.guest_session_id = {guestSessionId}
                FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> GetMemberDisplayNameAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        return context.Users
            .AsNoTracking()
            .Where(member => member.Id == memberId)
            .Select(member => member.DisplayName)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> CountActiveAsync(
        Guid wishlistId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return context.WishlistParticipants.CountAsync(
            participant => participant.WishlistId == wishlistId &&
                (participant.MemberId != null ||
                    context.GuestSessions.Any(session =>
                        session.Id == participant.GuestSessionId &&
                        session.ExpiresAt > now)),
            cancellationToken);
    }
}
