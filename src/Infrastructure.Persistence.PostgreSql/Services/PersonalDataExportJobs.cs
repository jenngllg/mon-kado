using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Options;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Coordinates fenced archive generation without holding a database transaction during filesystem writes.</summary>
/// <param name="context">The scoped database context.</param>
/// <param name="unitOfWork">The shared unit of work.</param>
/// <param name="userRepository">The account locking repository.</param>
/// <param name="store">The private export volume.</param>
/// <param name="options">The validated lifecycle limits.</param>
/// <param name="timeProvider">The UTC lifecycle clock.</param>
/// <param name="scopeFactory">The independent commit verification scope factory.</param>
public class PersonalDataExportJobs(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IMonKadoUserRepository userRepository,
    IPersonalDataExportStore store,
    IOptions<PersonalDataExportOptions> options,
    TimeProvider timeProvider,
    IServiceScopeFactory scopeFactory) : IPersonalDataExportJobs
{
    private const string DependencyName = "PostgreSQL";
    /// <inheritdoc/>
    public async Task<PersonalDataExportWorkItem?> ClaimAsync(CancellationToken cancellationToken)
    {
        try
        {

            return await ClaimNextAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                DependencyName,
                exception);
        }
    }

    /// <summary>Claims and commits one queue item before returning its independent worker lease.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The committed attempt, or null when no eligible attempt remains.</returns>
    private async Task<PersonalDataExportWorkItem?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var now = timeProvider
            .GetUtcNow()
            .UtcDateTime;
        var export = await context.MemberDataExports
            .FromSqlInterpolated($"""
            SELECT * FROM member_data_exports
                    WHERE member_id IS NOT NULL AND status IN ('Queued', 'Processing')
            AND available_at <= {now} AND (locked_until IS NULL OR locked_until <= {now})
                    ORDER BY available_at, created_at, id
            LIMIT 1 FOR UPDATE SKIP LOCKED
        """)
            .SingleOrDefaultAsync(cancellationToken);

        if (export is null)
            return null;
        var claimed = export.TryClaim(
            now,
            options.Value.LeaseDuration,
            options.Value.MaximumAttempts);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        if (!claimed)
            return null;

        return new PersonalDataExportWorkItem
        {
            ExportId = export.Id,
            MemberId = export.MemberId.GetValueOrDefault(),
            LeaseId = export.LeaseId.GetValueOrDefault(),
            AttemptCount = export.AttemptCount
        };
    }

    /// <inheritdoc/>
    public async Task<bool> RenewAsync(
        PersonalDataExportWorkItem workItem,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = timeProvider
                .GetUtcNow()
                .UtcDateTime;
            var count = await context.MemberDataExports
                .Where(export => export.Id == workItem.ExportId && export.MemberId == workItem.MemberId && export.Status == PersonalDataExportStatus.Processing && export.LeaseId == workItem.LeaseId && export.LockedUntil > now)
                .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    export => export.LockedUntil,
                    now.Add(options.Value.LeaseDuration)),
                cancellationToken);

            return count == 1;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                DependencyName,
                exception);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> CompleteAsync(
        PersonalDataExportWorkItem workItem,
        PersonalDataExportArchive archive,
        CancellationToken cancellationToken)
    {
        var commitAttempted = false;
        try
        {

            return await PublishAsync(
                workItem,
                archive,
                () => commitAttempted = true,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            if (commitAttempted && await VerifyPublicationAsync(
                workItem,
                cancellationToken))
                return true;

            throw new DependencyUnavailableException(
                DependencyName,
                exception);
        }
    }

    /// <summary>Publishes the archive and notification atomically, disposing the transaction before outcome verification.</summary>
    /// <param name="workItem">The generation lease.</param>
    /// <param name="archive">The completed immutable archive.</param>
    /// <param name="onCommitAttempt">Records when a failed acknowledgement can represent a committed transaction.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the live owned attempt was published.</returns>
    private async Task<bool> PublishAsync(
        PersonalDataExportWorkItem workItem,
        PersonalDataExportArchive archive,
        Action onCommitAttempt,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Match account deletion's lock order: account first, then dependent export.
        var member = await userRepository.GetByIdForUpdateAsync(
            workItem.MemberId,
            cancellationToken);

        if (member is not { EmailConfirmed: true })
            return false;
        var export = await ReadForUpdateAsync(
            workItem,
            cancellationToken);
        var now = timeProvider
            .GetUtcNow()
            .UtcDateTime;

        if (export is null || !export.Complete(
            workItem.LeaseId,
            now,
            archive.SnapshotAt,
            archive.SizeInBytes,
            options.Value.ArchiveLifetime))
            return false;
        context.AuthenticationEmailOutboxMessages.Add(AuthenticationEmailOutboxMessage.CreatePersonalDataExportReady(
                export.Id,
                workItem.MemberId,
                now));
        await unitOfWork.SaveChangesAsync(cancellationToken);
        onCommitAttempt();
        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    /// <inheritdoc/>
    public async Task FailAsync(
        PersonalDataExportWorkItem workItem,
        PersonalDataExportFailure failure,
        CancellationToken cancellationToken)
    {
        try
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var export = await ReadForUpdateAsync(
                workItem,
                cancellationToken);
            var retryIndex = Math.Min(
                workItem.AttemptCount - 1,
                options.Value.RetryDelays.Length - 1);
            export?.FailAttempt(
                workItem.LeaseId,
                timeProvider
                    .GetUtcNow()
                    .UtcDateTime,
                options.Value.RetryDelays[retryIndex],
                options.Value.MaximumAttempts,
                failure);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                DependencyName,
                exception);
        }
    }

    /// <inheritdoc/>
    public async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = timeProvider
                .GetUtcNow()
                .UtcDateTime;
            var quotaCutoff = now - options.Value.RequestWindow;
            var candidates = await context.MemberDataExports
                .AsNoTracking()
                .Where(export => export.MemberId == null || export.Status == PersonalDataExportStatus.Failed || export.Status == PersonalDataExportStatus.Expired || export.Status == PersonalDataExportStatus.Ready && export.ExpiresAt <= now)
                .Where(export => export.FilesCleanedAt == null || export.CreatedAt <= quotaCutoff)
                .OrderBy(export => export.CreatedAt)
                .ThenBy(export => export.Id)
                .Take(options.Value.CleanupBatchSize)
                .Select(export => new
                {
                    export.Id,
                    export.MemberId,
                    export.Status,
                    export.FilesCleanedAt,
                    export.CreatedAt
                })
                .ToListAsync(cancellationToken);
            foreach (var candidate in candidates)
            {

                if (candidate.Status is PersonalDataExportStatus.Ready)
                {
                    await context.MemberDataExports
                        .Where(export => export.Id == candidate.Id && export.Status == PersonalDataExportStatus.Ready)
                        .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            export => export.Status,
                            PersonalDataExportStatus.Expired),
                        cancellationToken);
                }

                if (candidate.FilesCleanedAt is null && !await store.DeleteAsync(
                    candidate.Id,
                    cancellationToken))
                    continue;

                if (candidate.MemberId is null || candidate.CreatedAt <= quotaCutoff)
                {
                    await context.MemberDataExports
                        .Where(export => export.Id == candidate.Id)
                        .ExecuteDeleteAsync(cancellationToken);
                    continue;
                }

                await context.MemberDataExports
                    .Where(export => export.Id == candidate.Id)
                    .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        export => export.FilesCleanedAt,
                        now),
                    cancellationToken);
            }

            await store.ReconcileAsync(
                now - options.Value.TemporaryGracePeriod,
                options.Value.CleanupBatchSize,
                IsReferencedAsync,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                DependencyName,
                exception);
        }
    }

    /// <summary>Locks one owned job before acknowledging an attempt.</summary>
    /// <param name="workItem">The work item whose identity is verified.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The locked job, or null when it is no longer owned.</returns>
    private Task<MemberDataExport?> ReadForUpdateAsync(
        PersonalDataExportWorkItem workItem,
        CancellationToken cancellationToken)
    {

        return context.MemberDataExports
            .FromSqlInterpolated($"""
            SELECT * FROM member_data_exports
            WHERE id = {workItem.ExportId} AND member_id = {workItem.MemberId}
            FOR UPDATE
        """)
            .SingleOrDefaultAsync(cancellationToken);
    }

    /// <summary>Confirms an uncertain publication without replaying it or extending the archive lifetime.</summary>
    /// <param name="workItem">The published attempt identity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the exact archive and its notification are durably published.</returns>
    private async Task<bool> VerifyPublicationAsync(
        PersonalDataExportWorkItem workItem,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var verification = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

            return await verification.MemberDataExports
                .AsNoTracking()
                .AnyAsync(
                export => export.Id == workItem.ExportId && export.MemberId == workItem.MemberId && export.Status == PersonalDataExportStatus.Ready && export.ArchiveId == workItem.LeaseId && verification.Users.Any(member => member.Id == workItem.MemberId && member.EmailConfirmed) && verification.AuthenticationEmailOutboxMessages.Any(message => message.MemberDataExportId == export.Id),
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            return false;
        }
    }

    /// <summary>Protects only the current live attempt and the published unexpired archive.</summary>
    /// <param name="exportId">The filesystem export directory identity.</param>
    /// <param name="archiveId">The immutable attempt identity.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether a durable reference still requires the file.</returns>
    private Task<bool> IsReferencedAsync(
        Guid exportId,
        Guid archiveId,
        CancellationToken cancellationToken)
    {
        var now = timeProvider
            .GetUtcNow()
            .UtcDateTime;

        return context.MemberDataExports
            .AsNoTracking()
            .AnyAsync(
            export => export.Id == exportId && export.MemberId != null && (export.Status == PersonalDataExportStatus.Ready && export.ArchiveId == archiveId && export.ExpiresAt > now || export.Status == PersonalDataExportStatus.Processing && export.LeaseId == archiveId && export.LockedUntil > now),
            cancellationToken);
    }
}
