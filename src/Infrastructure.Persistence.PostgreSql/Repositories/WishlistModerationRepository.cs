using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>Persists moderation decisions and their notification intent atomically.</summary>
/// <param name="context">The scoped database context shared with the unit of work.</param>
public class WishlistModerationRepository(MonKadoDbContext context) : IWishlistModerationRepository
{
    /// <inheritdoc />
    public async Task<bool> LockAdministratorAsync(
        Guid administratorId,
        CancellationToken cancellationToken)
    {
        var accounts = await context.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM public.users WHERE id = {administratorId} FOR UPDATE
            """)
            .ToArrayAsync(cancellationToken);

        return accounts.Length != 0;
    }

    /// <inheritdoc />
    public Task<Wishlist?> LockWishlistAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {

        return context.Wishlists
            .FromSqlInterpolated($"""
                SELECT wishlist.*, wishlist.xmin FROM public.wishlists AS wishlist
                WHERE wishlist.id = {wishlistId} FOR UPDATE
                """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<Wishlist?> GetWishlistAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {

        return context.Wishlists
            .AsNoTracking()
            .SingleOrDefaultAsync(
                wishlist => wishlist.Id == wishlistId,
                cancellationToken);
    }

    /// <inheritdoc />
    public Task<WishlistModerationEvent?> GetEventAsync(
        Guid eventId,
        CancellationToken cancellationToken)
    {

        return context.WishlistModerationEvents
            .AsNoTracking()
            .SingleOrDefaultAsync(
                decision => decision.Id == eventId,
                cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> GetNextSequenceAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var lastSequence = await context.WishlistModerationEvents
            .Where(decision => decision.WishlistId == wishlistId)
            .MaxAsync(
                decision => (long?)decision.Sequence,
                cancellationToken);

        return (lastSequence ?? 0) + 1;
    }

    /// <inheritdoc />
    public void AddDecision(WishlistModerationEvent decision)
    {
        context.WishlistModerationEvents.Add(decision);
        context.WishlistModerationEmails.Add(WishlistModerationEmail.Create(
            decision.Id,
            decision.OccurredAt));
    }

    /// <inheritdoc />
    public async Task<WishlistModerationEventPage> GetEventsAsync(
        Guid wishlistId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = context.WishlistModerationEvents
            .AsNoTracking()
            .Where(decision => decision.WishlistId == wishlistId);
        var count = await query.CountAsync(cancellationToken);
        var offset = ((long)page - 1) * pageSize;
        var items = Array.Empty<WishlistModerationEventDetails>();

        if (offset < count)
        {
            items = await query
                .OrderByDescending(decision => decision.Sequence)
                .Skip((int)offset)
                .Take(pageSize)
                .Select(decision => new WishlistModerationEventDetails
                {
                    Id = decision.Id,
                    AdministratorId = decision.AdministratorId,
                    Action = decision.Action,
                    Reason = decision.Reason,
                    OccurredAt = decision.OccurredAt
                })
                .ToArrayAsync(cancellationToken);
        }

        return new WishlistModerationEventPage
        {
            Items = items,
            CurrentPage = page,
            PageSize = pageSize,
            TotalCount = count
        };
    }
}
