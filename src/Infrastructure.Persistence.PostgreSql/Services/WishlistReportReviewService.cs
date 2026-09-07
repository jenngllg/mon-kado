using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Constants;

using Microsoft.EntityFrameworkCore;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Coordinates atomic report reviews and reconciles lost commit acknowledgements.</summary>
/// <param name="repository">The scoped review repository.</param>
/// <param name="accessService">The current PostgreSQL administrator authorization service.</param>
/// <param name="transactionFactory">The scoped transaction factory.</param>
/// <param name="unitOfWork">The scoped unit of work.</param>
/// <param name="timeProvider">The UTC clock.</param>
public class WishlistReportReviewService(
    IWishlistReportReviewRepository repository,
    IAdministratorAccessService accessService,
    IWishTransactionFactory transactionFactory,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IWishlistReportReviewService
{
    /// <inheritdoc/>
    public async Task<VersionedWishlistReportDetails> GetAsync(
        Guid wishlistId,
        Guid reportId,
        CancellationToken cancellationToken)
    {
        try
        {
            var report = await repository.GetReportAsync(
                wishlistId,
                reportId,
                cancellationToken) ?? throw new WishlistReportNotFoundException();

            return CreateDetails(report);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                DependencyNames.PostgreSql,
                exception);
        }
    }

    /// <inheritdoc/>
    public async Task<VersionedWishlistReportDetails> UpdateAsync(
        Guid administratorId,
        Guid wishlistId,
        Guid reportId,
        WishlistReportStatus status,
        string? note,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        WishlistReportReviewEvent? attempted = null;
        try
        {
            VersionedWishlistReportDetails result;
            await using (var transaction = await transactionFactory.BeginAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken))
            {

                if (!await repository.LockAdministratorAsync(
                    administratorId,
                    cancellationToken))
                    throw new InvalidAuthenticationSessionException();
                await EnsureAdministratorAsync(
                    administratorId,
                    cancellationToken);

                if (!await repository.LockWishlistAsync(
                    wishlistId,
                    cancellationToken))
                    throw new WishlistReportNotFoundException();
                var report = await repository.LockReportAsync(
                    wishlistId,
                    reportId,
                    cancellationToken) ?? throw new WishlistReportNotFoundException();

                if (report.Version != expectedVersion)
                    throw new WishlistReportVersionConflictException();
                var previousStatus = report.Status;
                var now = timeProvider
                    .GetUtcNow()
                    .UtcDateTime;
                // PostgreSQL truncates timestamps to microseconds; retain identical response and persisted dates.
                var occurredAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));

                if (!report.Review(
                    status,
                    note,
                    administratorId,
                    occurredAt))
                    return CreateDetails(report);
                var sequence = await repository.GetNextSequenceAsync(
                    reportId,
                    cancellationToken);
                var review = new WishlistReportReviewEvent(
                    Guid.CreateVersion7(),
                    report,
                    sequence,
                    previousStatus);
                repository.AddEvent(review);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                attempted = review;
                await transaction.CommitAsync(cancellationToken);
                result = CreateDetails(report);
            }

            return result;
        }
        catch (DbUpdateConcurrencyException)
        {

            throw new WishlistReportVersionConflictException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            if (attempted is null)
                throw new DependencyUnavailableException(
                    DependencyNames.PostgreSql,
                    exception);

            return await ResolveAmbiguousCommitAsync(
                administratorId,
                wishlistId,
                attempted,
                exception,
                cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<WishlistReportReviewEventPage> GetEventsAsync(
        Guid wishlistId,
        Guid reportId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            WishlistReportReviewEventPage result;
            await using (var transaction = await transactionFactory.BeginAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken))
            {
                _ = await repository.GetReportAsync(
                    wishlistId,
                    reportId,
                    cancellationToken) ?? throw new WishlistReportNotFoundException();
                result = await repository.GetEventsAsync(
                    reportId,
                    page,
                    pageSize,
                    cancellationToken);
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

    /// <summary>Checks current privileges after locking and when reconciling a commit.</summary>
    /// <param name="administratorId">The deciding account.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed when access is granted.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The account was deleted.</exception>
    /// <exception cref="AdministratorAccessDeniedException">Administrator privileges were removed.</exception>
    private async Task EnsureAdministratorAsync(
        Guid administratorId,
        CancellationToken cancellationToken)
    {
        var access = await accessService.GetAccessAsync(
            administratorId,
            cancellationToken);

        if (access is AdministratorAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is not AdministratorAccess.Granted)
            throw new AdministratorAccessDeniedException();
    }

    /// <summary>Confirms only this operation's still-current durable event using a coherent detached snapshot.</summary>
    /// <param name="administratorId">The deciding account.</param>
    /// <param name="wishlistId">The parent identifier.</param>
    /// <param name="attempted">The exact event identity generated before commit.</param>
    /// <param name="originalException">The lost commit acknowledgement.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current report when the attempted review is confirmed.</returns>
    /// <exception cref="DependencyUnavailableException">The outcome cannot be confirmed.</exception>
    private async Task<VersionedWishlistReportDetails> ResolveAmbiguousCommitAsync(
        Guid administratorId,
        Guid wishlistId,
        WishlistReportReviewEvent attempted,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnsureAdministratorAsync(
                administratorId,
                cancellationToken);
            VersionedWishlistReportDetails result;
            await using (var transaction = await transactionFactory.BeginAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken))
            {
                var report = await repository.GetReportAsync(
                    wishlistId,
                    attempted.ReportId,
                    cancellationToken) ?? throw new WishlistReportNotFoundException();
                var latest = await repository.GetLatestEventAsync(
                    attempted.ReportId,
                    cancellationToken);

                if (latest?.Id != attempted.Id)
                    throw new DependencyUnavailableException(
                        DependencyNames.PostgreSql,
                        originalException);
                result = CreateDetails(report);
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

    /// <summary>Separates the private persistence version from the public report body.</summary>
    /// <param name="report">The persisted report.</param>
    /// <returns>The safe representation and ETag source.</returns>
    private static VersionedWishlistReportDetails CreateDetails(WishlistReport report)
    {

        return new VersionedWishlistReportDetails
        {
            Version = report.Version,
            Report = new WishlistReportDetails
            {
                Id = report.Id,
                Reason = report.Reason,
                Details = report.Details,
                CreatedAt = report.CreatedAt,
                Status = report.Status,
                ReviewNote = report.ReviewNote,
                ReviewedAt = report.ReviewedAt,
                ReviewedByAdministratorId = report.ReviewedByAdministratorId
            }
        };
    }
}
