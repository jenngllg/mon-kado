using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;

/// <summary>Stores private report reviews without generating moderation email deliveries.</summary>
/// <param name="context">The scoped PostgreSQL context.</param>
public class WishlistReportReviewRepository(MonKadoDbContext context) : IWishlistReportReviewRepository
{
    /// <inheritdoc/>
    public async Task<bool> LockAdministratorAsync(
        Guid administratorId,
        CancellationToken cancellationToken)
    {
        var ids = await context.Database
            .SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM public.users WHERE id = {administratorId} FOR UPDATE
        """)
            .ToArrayAsync(cancellationToken);

        return ids.Length != 0;
    }

    /// <inheritdoc/>
    public async Task<bool> LockWishlistAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var ids = await context.Database
            .SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM public.wishlists WHERE id = {wishlistId} FOR UPDATE
        """)
            .ToArrayAsync(cancellationToken);

        return ids.Length != 0;
    }

    /// <inheritdoc/>
    public Task<WishlistReport?> LockReportAsync(
        Guid wishlistId,
        Guid reportId,
        CancellationToken cancellationToken)
    {

        return context.WishlistReports
            .FromSqlInterpolated($"""
            SELECT report.*, report.xmin FROM public.wishlist_reports AS report
            WHERE report.id = {reportId} AND report.wishlist_id = {wishlistId} FOR UPDATE
        """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<WishlistReport?> GetReportAsync(
        Guid wishlistId,
        Guid reportId,
        CancellationToken cancellationToken)
    {

        return context.WishlistReports
            .AsNoTracking()
            .SingleOrDefaultAsync(
            report => report.Id == reportId && report.WishlistId == wishlistId,
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<WishlistReportReviewEvent?> GetLatestEventAsync(
        Guid reportId,
        CancellationToken cancellationToken)
    {

        return context.WishlistReportReviewEvents
            .AsNoTracking()
            .Where(review => review.ReportId == reportId)
            .OrderByDescending(review => review.Sequence)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<long> GetNextSequenceAsync(
        Guid reportId,
        CancellationToken cancellationToken)
    {
        var previous = await context.WishlistReportReviewEvents
            .Where(review => review.ReportId == reportId)
            .MaxAsync(
            review => (long?)review.Sequence,
            cancellationToken);

        return (previous ?? 0) + 1;
    }

    /// <inheritdoc/>
    public void AddEvent(WishlistReportReviewEvent review)
    {
        context.WishlistReportReviewEvents.Add(review);
    }

    /// <inheritdoc/>
    public async Task<WishlistReportReviewEventPage> GetEventsAsync(
        Guid reportId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = context.WishlistReportReviewEvents
            .AsNoTracking()
            .Where(review => review.ReportId == reportId);
        var totalCount = await query.CountAsync(cancellationToken);
        var offset = ((long)page - 1) * pageSize;
        var items = Array.Empty<WishlistReportReviewEventDetails>();

        if (offset < totalCount)
            items = await query
                .OrderByDescending(review => review.Sequence)
                .Skip((int)offset)
                .Take(pageSize)
                .Select(review => new WishlistReportReviewEventDetails
                {
                    Id = review.Id,
                    PreviousStatus = review.PreviousStatus,
                    Status = review.Status,
                    Note = review.Note,
                    AdministratorId = review.AdministratorId,
                    OccurredAt = review.OccurredAt
                })
                .ToArrayAsync(cancellationToken);

        return new WishlistReportReviewEventPage
        {
            Items = items,
            CurrentPage = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
