using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;

using Microsoft.EntityFrameworkCore;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Projects current reported content without loading reservations or share credentials.</summary>
/// <param name="context">The scoped PostgreSQL context.</param>
/// <param name="transactionFactory">The scoped transaction factory.</param>
/// <param name="imageStore">The normalized image store.</param>
public class ReportedWishlistService(
    MonKadoDbContext context,
    IWishTransactionFactory transactionFactory,
    IGiftImageStore imageStore) : IReportedWishlistService
{
    /// <inheritdoc/>
    public Task<ReportedWishlistPage> GetPageAsync(
        WishlistReportReason? reason,
        bool? isSuspended,
        WishlistReportStatusFilter status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {

        return ReadAsync(
            async token =>
            {
                var reports = context.WishlistReports.AsNoTracking();

                if (reason.HasValue)
                    reports = reports.Where(report => report.Reason == reason.Value);

                if (status is not WishlistReportStatusFilter.All)
                {
                    // Capture the domain enum so EF uses its string converter instead of casting the filter in SQL.
                    var selectedStatus = (WishlistReportStatus)status;
                    reports = reports.Where(report => report.Status == selectedStatus);
                }
                var groups = reports
                    .GroupBy(report => report.WishlistId)
                    .Select(group => new
                    {
                        WishlistId = group.Key,
                        ReportCount = group.Count(),
                        LastReportedAt = group.Max(report => report.CreatedAt)
                    });
                var query = groups
                    .Join(
                    context.Wishlists.AsNoTracking(),
                    group => group.WishlistId,
                    wishlist => wishlist.Id,
                    (
                        group,
                        wishlist) => new
                        {
                            group,
                            wishlist
                        })
                    .Join(
                    context.Users.AsNoTracking(),
                    item => item.wishlist.OwnerId,
                    owner => owner.Id,
                    (
                        item,
                        owner) => new ReportedWishlistSummary
                        {
                            WishlistId = item.wishlist.Id,
                            Name = item.wishlist.Name,
                            OwnerId = owner.Id,
                            OwnerDisplayName = owner.DisplayName,
                            IsSuspended = item.wishlist.IsSuspended,
                            ReportCount = item.group.ReportCount,
                            LastReportedAt = item.group.LastReportedAt
                        });

                if (isSuspended.HasValue)
                    query = query.Where(item => item.IsSuspended == isSuspended.Value);
                var totalCount = await query.CountAsync(token);
                var offset = (long)(page - 1) * pageSize;
                var items = Array.Empty<ReportedWishlistSummary>();

                if (offset < totalCount)
                    items = await query
                        .OrderByDescending(item => item.LastReportedAt)
                        .ThenBy(item => item.WishlistId)
                        .Skip((int)offset)
                        .Take(pageSize)
                        .ToArrayAsync(token);

                return new ReportedWishlistPage
                {
                    Items = items,
                    CurrentPage = page,
                    PageSize = pageSize,
                    TotalCount = totalCount
                };
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ReportedWishlistDetails> GetAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {

        return ReadAsync(
            async token =>
            {
                var details = await context.Wishlists
                    .AsNoTracking()
                    .Where(wishlist => wishlist.Id == wishlistId && context.WishlistReports.Any(report => report.WishlistId == wishlist.Id))
                    .Join(
                    context.Users.AsNoTracking(),
                    wishlist => wishlist.OwnerId,
                    owner => owner.Id,
                    (
                        wishlist,
                        owner) => new ReportedWishlistDetails(
                            new WishlistDetails(
                                wishlist.Id,
                                wishlist.Name,
                                wishlist.Occasion,
                                wishlist.EventDate,
                                wishlist.Message,
                                wishlist.CreatedAt,
                                wishlist.UpdatedAt,
                                wishlist.Version)
                            {
                                IsSuspended = wishlist.IsSuspended,
                                SuspensionReason = wishlist.SuspensionReason,
                                SuspendedAt = wishlist.SuspendedAt
                            },
                            owner.Id,
                            owner.DisplayName,
                            context.Wishes
                            .Where(wish => wish.WishlistId == wishlist.Id)
                            .OrderBy(wish => wish.Position)
                            .ThenBy(wish => wish.Id)
                            .Select(wish => new ReportedWishDetails
                            {
                                Id = wish.Id,
                                Name = wish.Name,
                                Note = wish.Note,
                                Url = wish.Url,
                                Price = wish.Price,
                                Quantity = wish.Quantity,
                                Position = wish.Position,
                                CreatedAt = wish.CreatedAt,
                                UpdatedAt = wish.UpdatedAt,
                                ImageId = wish.ImageId
                            })
                            .ToArray()))
                    .SingleOrDefaultAsync(token);

                return details ?? throw new WishlistNotFoundException();
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<WishlistReportPage> GetReportsAsync(
        Guid wishlistId,
        WishlistReportReason? reason,
        WishlistReportStatusFilter status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {

        return ReadAsync(
            async token =>
            {
                var query = context.WishlistReports
                    .AsNoTracking()
                    .Where(report => report.WishlistId == wishlistId);

                if (!await query.AnyAsync(token))
                    throw new WishlistNotFoundException();

                if (reason.HasValue)
                    query = query.Where(report => report.Reason == reason.Value);

                if (status is not WishlistReportStatusFilter.All)
                {
                    var selectedStatus = (WishlistReportStatus)status;
                    query = query.Where(report => report.Status == selectedStatus);
                }
                var totalCount = await query.CountAsync(token);
                var offset = (long)(page - 1) * pageSize;
                var items = Array.Empty<WishlistReportDetails>();

                if (offset < totalCount)
                    items = await query
                        .OrderByDescending(report => report.CreatedAt)
                        .ThenByDescending(report => report.Id)
                        .Skip((int)offset)
                        .Take(pageSize)
                        .Select(report => new WishlistReportDetails
                        {
                            Id = report.Id,
                            Reason = report.Reason,
                            Details = report.Details,
                            CreatedAt = report.CreatedAt,
                            Status = report.Status,
                            ReviewNote = report.ReviewNote,
                            ReviewedAt = report.ReviewedAt,
                            ReviewedByAdministratorId = report.ReviewedByAdministratorId
                        })
                        .ToArrayAsync(token);

                return new WishlistReportPage
                {
                    Items = items,
                    CurrentPage = page,
                    PageSize = pageSize,
                    TotalCount = totalCount
                };
            },
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Stream> OpenImageAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        var imageId = await ReadAsync(
            token => context.Wishes
                .AsNoTracking()
                .Where(wish => wish.Id == wishId && wish.WishlistId == wishlistId && context.WishlistReports.Any(report => report.WishlistId == wishlistId))
                .Select(wish => wish.ImageId)
                .SingleOrDefaultAsync(token),
            cancellationToken) ?? throw new GiftImageNotFoundException();
        var stream = await imageStore.OpenReadAsync(
            imageId,
            cancellationToken);

        return stream ?? throw new GiftImageStorageUnavailableException(new FileNotFoundException("A referenced normalized gift image is missing."));
    }

    /// <summary>Keeps all queries in one stable snapshot and translates only database availability failures.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="read">The bounded database read.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The fully materialized result.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    private async Task<T> ReadAsync<T>(
        Func<CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        try
        {
            T result;
            await using (var transaction = await transactionFactory.BeginAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken))
            {
                result = await read(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                DependencyNames.PostgreSql,
                exception);
        }
    }
}
