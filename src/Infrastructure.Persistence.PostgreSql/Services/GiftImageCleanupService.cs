using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Provides durable PostgreSQL coordination for obsolete gift-image cleanup.
/// </summary>
/// <param name="context">The database context.</param>
/// <param name="unitOfWork">The unit of work.</param>
/// <param name="repository">The deletion outbox repository.</param>
public class GiftImageCleanupService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IGiftImageDeletionOutboxRepository repository) : IGiftImageCleanupService
{
    /// <inheritdoc />
    public async Task<GiftImageDeletion?> ClaimNextAsync(
        DateTime now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        var strategy = context.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(
            token => ClaimNextOnceAsync(
                now,
                leaseDuration,
                token),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task CompleteAsync(
        Guid deletionId,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var message = await repository.GetByIdForUpdateAsync(
            deletionId,
            cancellationToken);

        if (message is null)
            return;

        repository.Remove(message);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task ScheduleRetryAsync(
        Guid deletionId,
        DateTime availableAt,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        var message = await repository.GetByIdForUpdateAsync(
            deletionId,
            cancellationToken);

        if (message is null)
            return;

        message.ScheduleRetry(availableAt);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsReferencedAsync(
        Guid imageId,
        CancellationToken cancellationToken)
    {
        return context.Wishes
            .AsNoTracking()
            .AnyAsync(
                wish => wish.ImageId == imageId,
                cancellationToken);
    }

    /// <summary>
    /// Claims one deletion inside a retryable PostgreSQL transaction.
    /// </summary>
    /// <param name="now">The current UTC date and time.</param>
    /// <param name="leaseDuration">The claim lease duration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The claimed deletion, or <see langword="null" /> when none is available.</returns>
    private async Task<GiftImageDeletion?> ClaimNextOnceAsync(
        DateTime now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var message = await repository.GetNextForUpdateAsync(
            now,
            cancellationToken);

        if (message is null)
        {
            await transaction.CommitAsync(cancellationToken);

            return null;
        }

        message.Claim(now.Add(leaseDuration));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new GiftImageDeletion(
            message.Id,
            message.ImageId,
            message.AttemptCount);
    }
}
