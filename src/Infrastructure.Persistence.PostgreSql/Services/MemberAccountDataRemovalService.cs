using JennGllg.Fr.MonKado.Back.Domain.Entities;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Stages account removal and durable file cleanup inside the caller's transaction.</summary>
/// <param name="context">The shared transactional context.</param>
/// <param name="timeProvider">The UTC clock.</param>
public class MemberAccountDataRemovalService(
    MonKadoDbContext context,
    TimeProvider timeProvider) : IMemberAccountDataRemovalService
{
    /// <summary>Locks affected parents before queuing images and removing dependent data.</summary>
    /// <param name="member">The exclusively locked member.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the staged and transactional removal.</returns>
    public async Task StageAsync(
        MonKadoUser member,
        CancellationToken cancellationToken)
    {
        var wishlists = await context.Wishlists
            .FromSqlInterpolated($"""
            SELECT w.*, w.xmin FROM public.wishlists w
            WHERE w.owner_id = {member.Id} OR EXISTS (
                    SELECT 1 FROM public.wishlist_participants p
            WHERE p.wishlist_id = w.id AND p.member_id = {member.Id})
            ORDER BY w.id FOR UPDATE OF w
        """)
            .ToListAsync(cancellationToken);
        var ownedIds = wishlists
            .Where(wishlist => wishlist.OwnerId == member.Id)
            .Select(wishlist => wishlist.Id)
            .ToArray();
        var wishes = await context.Wishes
            .FromSqlInterpolated($"""
            SELECT w.*, w.xmin FROM public.wishes w
            WHERE w.wishlist_id = ANY({ownedIds})
            ORDER BY w.wishlist_id, w.id FOR UPDATE OF w
        """)
            .ToListAsync(cancellationToken);
        foreach (var imageId in wishes
            .Where(wish => wish.ImageId.HasValue)
            .Select(wish => wish.ImageId.GetValueOrDefault()))
        {
            context.GiftImageDeletionOutboxMessages.Add(GiftImageDeletionOutboxMessage.Create(
                    imageId,
                    timeProvider
                        .GetUtcNow()
                        .UtcDateTime));
        }

        await context.GiftReservationHistories
            .Where(history => ownedIds.Contains(history.WishlistId) && history.MemberId != member.Id)
            .ExecuteUpdateAsync(
            setters => setters
                .SetProperty(
                history => history.WishlistName,
                "Deleted wishlist")
                .SetProperty(
                history => history.WishName,
                "Deleted gift"),
            cancellationToken);
        await context.WishlistParticipants
            .Where(participant => participant.MemberId == member.Id)
            .ExecuteDeleteAsync(cancellationToken);
        context.Wishlists.RemoveRange(wishlists.Where(wishlist => wishlist.OwnerId == member.Id));

        if (member.ProfileImageId is { } profileImageId)
        {
            context.GiftImageDeletionOutboxMessages.Add(GiftImageDeletionOutboxMessage.Create(
                profileImageId,
                timeProvider.GetUtcNow().UtcDateTime));
        }

        context.Users.Remove(member);
    }
}
