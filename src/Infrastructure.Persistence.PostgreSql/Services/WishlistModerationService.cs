using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Serializes administrator decisions with wishlist mutations and durable notification intent.</summary>
/// <param name="repository">The moderation repository.</param>
/// <param name="accessService">The current administrator access service.</param>
/// <param name="transactionFactory">The scoped transaction factory.</param>
/// <param name="unitOfWork">The scoped unit of work.</param>
/// <param name="timeProvider">The UTC clock.</param>
public class WishlistModerationService(
    IWishlistModerationRepository repository,
    IAdministratorAccessService accessService,
    IWishTransactionFactory transactionFactory,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IWishlistModerationService
{
    /// <inheritdoc/>
    public async Task<WishlistModerationDetails> GetAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        try
        {
            var wishlist = await repository.GetWishlistAsync(
                wishlistId,
                cancellationToken) ?? throw new WishlistNotFoundException();

            return CreateDetails(wishlist);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <inheritdoc/>
    public async Task<WishlistModerationDetails> UpdateAsync(
        Guid administratorId,
        Guid wishlistId,
        bool isSuspended,
        string? reason,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        WishlistModerationEvent? attemptedDecision = null;
        var commitAttempted = false;
        try
        {
            WishlistModerationDetails completedResult;
            await using (var transaction = await transactionFactory.BeginAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken))
            {
                var administratorExists = await repository.LockAdministratorAsync(
                    administratorId,
                    cancellationToken);

                if (!administratorExists)
                    throw new InvalidAuthenticationSessionException();
                await EnsureAdministratorAsync(
                    administratorId,
                    cancellationToken);
                var wishlist = await repository.LockWishlistAsync(
                    wishlistId,
                    cancellationToken) ?? throw new WishlistNotFoundException();

                if (wishlist.Version != expectedVersion)
                    throw new WishlistVersionConflictException();
                var wasSuspended = wishlist.IsSuspended;
                var now = timeProvider
                    .GetUtcNow()
                    .UtcDateTime;
                // PostgreSQL timestamps have microsecond precision; responses must match subsequent reads.
                var occurredAt = now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
                var changed = wishlist.Moderate(
                    isSuspended,
                    reason,
                    occurredAt);

                if (!changed)
                    return CreateDetails(wishlist);
                var sequence = await repository.GetNextSequenceAsync(
                    wishlistId,
                    cancellationToken);
                var action = GetAction(
                    wasSuspended,
                    isSuspended);
                attemptedDecision = new WishlistModerationEvent(
                    Guid.CreateVersion7(),
                    wishlistId,
                    administratorId,
                    sequence,
                    action,
                    wishlist.SuspensionReason,
                    occurredAt);
                repository.AddDecision(attemptedDecision);
                await unitOfWork.SaveChangesAsync(cancellationToken);
                commitAttempted = true;
                await transaction.CommitAsync(cancellationToken);
                completedResult = CreateDetails(wishlist);
            }

            return completedResult;
        }
        catch (DbUpdateConcurrencyException)
        {

            throw new WishlistVersionConflictException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            if (!commitAttempted || attemptedDecision is null)
            {

                throw new DependencyUnavailableException(
                    "PostgreSQL",
                    exception);
            }

            return await ResolveAmbiguousCommitAsync(
                administratorId,
                attemptedDecision,
                exception,
                cancellationToken);
        }
    }

    /// <inheritdoc/>
    public async Task<WishlistModerationEventPage> GetEventsAsync(
        Guid wishlistId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        try
        {
            WishlistModerationEventPage completedResult;
            await using (var transaction = await transactionFactory.BeginAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken))
            {
                _ = await repository.GetWishlistAsync(
                    wishlistId,
                    cancellationToken) ?? throw new WishlistNotFoundException();
                var result = await repository.GetEventsAsync(
                    wishlistId,
                    page,
                    pageSize,
                    cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                completedResult = result;
            }

            return completedResult;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>Checks current administrator privileges after the account lock is acquired.</summary>
    /// <param name="administratorId">The deciding account identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed when access is granted.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The account was deleted.</exception>
    /// <exception cref="AdministratorAccessDeniedException">The administrator role was removed.</exception>
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

    /// <summary>Reconciles a lost commit acknowledgement using detached durable state.</summary>
    /// <param name="administratorId">The deciding account identifier.</param>
    /// <param name="attempted">The exact attempted event identity.</param>
    /// <param name="originalException">The lost commit acknowledgement.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The durable moderation state if the decision can be confirmed.</returns>
    /// <exception cref="DependencyUnavailableException">The decision cannot be confirmed.</exception>
    private async Task<WishlistModerationDetails> ResolveAmbiguousCommitAsync(
        Guid administratorId,
        WishlistModerationEvent attempted,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnsureAdministratorAsync(
                administratorId,
                cancellationToken);
            var committed = await repository.GetEventAsync(
                attempted.Id,
                cancellationToken);
            var wishlist = await repository.GetWishlistAsync(
                attempted.WishlistId,
                cancellationToken) ?? throw new WishlistNotFoundException();
            var expectedSuspension = attempted.Action is not WishlistModerationAction.Reactivated;

            if (committed is not null && committed.WishlistId == attempted.WishlistId && committed.Action == attempted.Action && string.Equals(
                committed.Reason,
                attempted.Reason,
                StringComparison.Ordinal) && wishlist.IsSuspended == expectedSuspension && string.Equals(
                wishlist.SuspensionReason,
                attempted.Reason,
                StringComparison.Ordinal))
                return CreateDetails(wishlist);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }

        throw new DependencyUnavailableException(
            "PostgreSQL",
            originalException);
    }

    /// <summary>Classifies an effective state transition.</summary>
    /// <param name="wasSuspended">The previous suspension state.</param>
    /// <param name="isSuspended">The new suspension state.</param>
    /// <returns>The history action.</returns>
    private static WishlistModerationAction GetAction(
        bool wasSuspended,
        bool isSuspended)
    {

        if (!isSuspended)
            return WishlistModerationAction.Reactivated;

        return wasSuspended ? WishlistModerationAction.ReasonUpdated : WishlistModerationAction.Suspended;
    }

    /// <summary>Maps the current state without exposing persistence entities.</summary>
    /// <param name="wishlist">The persisted wishlist.</param>
    /// <returns>The private moderation state.</returns>
    private static WishlistModerationDetails CreateDetails(Wishlist wishlist)
    {

        return new WishlistModerationDetails
        {
            WishlistId = wishlist.Id,
            IsSuspended = wishlist.IsSuspended,
            SuspensionReason = wishlist.SuspensionReason,
            SuspendedAt = wishlist.SuspendedAt,
            Version = wishlist.Version
        };
    }
}
