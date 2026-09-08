using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;

using Microsoft.EntityFrameworkCore;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Unifies retained administrative events entirely in PostgreSQL.</summary>
/// <param name="context">The scoped database context.</param>
/// <param name="transactionFactory">The consistent read transaction factory.</param>
public class AdministrativeAuditService(
    MonKadoDbContext context,
    IWishTransactionFactory transactionFactory) : IAdministrativeAuditService
{
    /// <summary>Counts and reads one globally ordered page from a repeatable snapshot.</summary>
    /// <param name="filter">The normalized validated filters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The filtered event page.</returns>
    /// <exception cref="DependencyUnavailableException">The database cannot serve the snapshot.</exception>
    public async Task<AdministrativeAuditPage> GetPageAsync(
        AdministrativeAuditFilter filter,
        CancellationToken cancellationToken)
    {
        try
        {
            AdministrativeAuditPage result;
            await using (var transaction = await transactionFactory.BeginAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken))
            {
                var query = ApplyFilters(
                    CreateQuery(),
                    filter);
                var count = await query.CountAsync(cancellationToken);
                var offset = (long)(filter.Page - 1) * filter.PageSize;
                var items = Array.Empty<AdministrativeAuditEventDetails>();

                if (offset < count)
                    items = await query
                        .OrderByDescending(item => item.CreatedAt)
                        .ThenByDescending(item => item.Id)
                        .ThenBy(item => item.Action)
                        .Skip((int)offset)
                        .Take(filter.PageSize)
                        .ToArrayAsync(cancellationToken);
                result = new AdministrativeAuditPage
                {
                    Items = items,
                    CurrentPage = filter.Page,
                    PageSize = filter.PageSize,
                    TotalCount = count
                };
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

    /// <summary>Projects existing sources without joining to targets whose deletion must not hide retained events.</summary>
    /// <returns>The composable SQL UNION ALL with an optional current actor.</returns>
    private IQueryable<AdministrativeAuditEventDetails> CreateQuery()
    {
        var moderation = context.WishlistModerationEvents
            .AsNoTracking()
            .Select(entry => new
            {
                Entry = entry,
                Action = entry.Action == WishlistModerationAction.Suspended ? AdministrativeAuditAction.WishlistSuspended : AdministrativeAuditAction.WishlistSuspensionReasonUpdated
            })
            .Select(item => new AdministrativeAuditEventDetails
            {
                Id = item.Entry.Id,
                CreatedAt = item.Entry.OccurredAt,
                Action = item.Entry.Action == WishlistModerationAction.Reactivated ? AdministrativeAuditAction.WishlistReactivated : item.Action,
                AdministratorId = item.Entry.AdministratorId,
                AdministratorDisplayName = null,
                WishlistId = item.Entry.WishlistId,
                MemberId = null,
                ExportId = null,
                Reason = item.Entry.Reason,
                RequestReference = null
            });
        var exports = context.AdministrativeDataExportEvents
            .AsNoTracking()
            .Select(entry => new AdministrativeAuditEventDetails
            {
                Id = entry.Id,
                CreatedAt = entry.CreatedAt,
                Action = entry.Action == AdministrativeDataExportAction.Requested ? AdministrativeAuditAction.MemberDataExportRequested : AdministrativeAuditAction.MemberDataExportDownloadStarted,
                AdministratorId = entry.AdministratorId,
                AdministratorDisplayName = null,
                WishlistId = null,
                MemberId = entry.MemberId,
                ExportId = entry.ExportId,
                Reason = null,
                RequestReference = entry.RequestReference
            });
        var erasures = context.AdministrativeAccountErasureEvents
            .AsNoTracking()
            .Select(entry => new AdministrativeAuditEventDetails
            {
                Id = entry.Id,
                CreatedAt = entry.CreatedAt,
                Action = AdministrativeAuditAction.MemberErased,
                AdministratorId = entry.AdministratorId,
                AdministratorDisplayName = null,
                WishlistId = null,
                MemberId = entry.MemberId,
                ExportId = null,
                Reason = null,
                RequestReference = entry.RequestReference
            });

        var revocations = context.AdministrativeSessionRevocationEvents
            .AsNoTracking()
            .Select(entry => new AdministrativeAuditEventDetails
            {
                Id = entry.Id,
                CreatedAt = entry.CreatedAt,
                Action = AdministrativeAuditAction.MemberSessionsRevoked,
                AdministratorId = entry.AdministratorId,
                AdministratorDisplayName = null,
                WishlistId = null,
                MemberId = entry.MemberId,
                ExportId = null,
                Reason = null,
                RequestReference = entry.RequestReference
            });

        return moderation
            .Concat(exports)
            .Concat(erasures)
            .Concat(revocations)
            .LeftJoin(
                context.Users.AsNoTracking(),
                entry => entry.AdministratorId,
                actor => actor.Id,
                (
                    entry,
                    actor) => new AdministrativeAuditEventDetails
                    {
                        Id = entry.Id,
                        CreatedAt = entry.CreatedAt,
                        Action = entry.Action,
                        AdministratorId = entry.AdministratorId,
                        AdministratorDisplayName = actor == null ? null : actor.DisplayName,
                        WishlistId = entry.WishlistId,
                        MemberId = entry.MemberId,
                        ExportId = entry.ExportId,
                        Reason = entry.Reason,
                        RequestReference = entry.RequestReference
                    });
    }

    /// <summary>Composes conjunctive filters before any counting or pagination.</summary>
    /// <param name="query">The unified query.</param>
    /// <param name="filter">The normalized filters.</param>
    /// <returns>The filtered SQL query.</returns>
    private static IQueryable<AdministrativeAuditEventDetails> ApplyFilters(
        IQueryable<AdministrativeAuditEventDetails> query,
        AdministrativeAuditFilter filter)
    {

        if (filter.Action.HasValue)
            query = query.Where(item => item.Action == filter.Action.Value);

        if (filter.AdministratorId.HasValue)
            query = query.Where(item => item.AdministratorId == filter.AdministratorId.Value);

        if (filter.MemberId.HasValue)
            query = query.Where(item => item.MemberId == filter.MemberId.Value);

        if (filter.WishlistId.HasValue)
            query = query.Where(item => item.WishlistId == filter.WishlistId.Value);

        if (filter.ExportId.HasValue)
            query = query.Where(item => item.ExportId == filter.ExportId.Value);

        if (filter.RequestReference is not null)
            query = query.Where(item => EF.Functions.Collate(
                item.RequestReference!,
                "C") == filter.RequestReference);

        if (filter.From.HasValue)
        {
            var from = filter.From.Value;
            var floor = from.AddTicks(-(from.Ticks % TimeSpan.TicksPerMicrosecond));
            // PostgreSQL stores microseconds; retain the boundary semantics instead of silently truncating a finer input.
            query = from == floor
                ? query.Where(item => item.CreatedAt >= floor)
                : query.Where(item => item.CreatedAt > floor);
        }

        if (filter.To.HasValue)
        {
            var to = filter.To.Value;
            var floor = to.AddTicks(-(to.Ticks % TimeSpan.TicksPerMicrosecond));
            query = to == floor
                ? query.Where(item => item.CreatedAt < floor)
                : query.Where(item => item.CreatedAt <= floor);
        }

        return query;
    }
}
