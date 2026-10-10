using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>Reads accessible list summaries without fetching wish contents.</summary>
/// <param name="context">The scoped database context.</param>
public class WishlistSubscriptionRepository(MonKadoDbContext context) : IWishlistSubscriptionRepository
{
    /// <inheritdoc />
    public void Add(WishlistSubscription subscription)
    {
        context.WishlistSubscriptions.Add(subscription);
    }

    /// <inheritdoc />
    public void Remove(WishlistSubscription subscription)
    {
        context.WishlistSubscriptions.Remove(subscription);
    }

    /// <inheritdoc />
    public Task<WishlistSubscription?> GetForUpdateAsync(
        Guid memberId,
        Guid id,
        CancellationToken cancellationToken)
    {

        return context.WishlistSubscriptions
            .SingleOrDefaultAsync(
                subscription => subscription.MemberId == memberId && subscription.Id == id,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<WishlistSubscriptionDetails?> GetAsync(
        Guid memberId,
        Guid id,
        CancellationToken cancellationToken)
    {

        return Summaries(memberId)
            .SingleOrDefaultAsync(
                subscription => subscription.Id == id,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<WishlistSubscriptionDetails?> GetCurrentAsync(
        Guid memberId,
        Guid shareLinkId,
        CancellationToken cancellationToken)
    {

        return Summaries(memberId)
            .SingleOrDefaultAsync(
                subscription => subscription.ShareLinkId == shareLinkId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WishlistSubscriptionPage> GetPageAsync(
        Guid memberId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            cancellationToken);
        var summaries = Summaries(memberId);
        var count = await summaries.CountAsync(cancellationToken);
        var offset = (long)(page - 1) * pageSize;
        var items = offset < count
            ? await summaries
                .OrderByDescending(subscription => subscription.CreatedAt)
                .ThenBy(subscription => subscription.Id)
                .Skip((int)offset)
                .Take(pageSize)
                .ToArrayAsync(cancellationToken)
            : [];
        await transaction.CommitAsync(cancellationToken);

        return new WishlistSubscriptionPage
        {
            Items = items,
            CurrentPage = page,
            PageSize = pageSize,
            TotalCount = count
        };
    }

    /// <summary>Projects only summaries whose original share capability remains valid.</summary>
    /// <param name="memberId">The subscriber.</param>
    /// <returns>The scoped accessible summary query.</returns>
    private IQueryable<WishlistSubscriptionDetails> Summaries(Guid memberId)
    {

        return context.WishlistSubscriptions
            .AsNoTracking()
            .Where(subscription => subscription.MemberId == memberId)
            .Join(
                context.WishlistShareLinks.AsNoTracking(),
                subscription => subscription.ShareLinkId,
                link => link.Id,
                (subscription, link) => new { Subscription = subscription, Link = link })
            .Where(item => item.Subscription.WishlistId == item.Link.WishlistId &&
                item.Subscription.ShareSecretHash == item.Link.SecretHash)
            .Join(
                context.Wishlists.AsNoTracking(),
                item => item.Subscription.WishlistId,
                wishlist => wishlist.Id,
                (item, wishlist) => new { item.Subscription, item.Link, Wishlist = wishlist })
            .Where(item => !item.Wishlist.IsArchived && !item.Wishlist.IsSuspended)
            .Join(
                context.Users.AsNoTracking(),
                item => item.Wishlist.OwnerId,
                owner => owner.Id,
                (item, owner) => new WishlistSubscriptionDetails
                {
                    Id = item.Subscription.Id,
                    WishlistId = item.Wishlist.Id,
                    Name = item.Wishlist.Name,
                    OwnerDisplayName = owner.DisplayName,
                    Occasion = item.Wishlist.Occasion,
                    EventDate = item.Wishlist.EventDate,
                    CreatedAt = item.Subscription.CreatedAt,
                    ShareLinkId = item.Link.Id,
                    ProtectedSecret = item.Link.ProtectedSecret
                });
    }
}
