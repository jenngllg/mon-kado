using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Authorizes administrative exports and couples archive release to durable accountability.</summary>
/// <param name="context">The scoped database context.</param>
/// <param name="unitOfWork">The transactional unit of work.</param>
/// <param name="requestRepository">The shared member quota and locking coordinator.</param>
/// <param name="archiveReader">The private archive reader.</param>
/// <param name="administratorAccess">The current database-backed administrator access service.</param>
/// <param name="scopeFactory">The independent ambiguous-commit verification scope factory.</param>
/// <param name="timeProvider">The UTC clock.</param>
public class AdministrativeDataExportService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IPersonalDataExportRequestRepository requestRepository,
    IPersonalDataExportArchiveReader archiveReader,
    IAdministratorAccessService administratorAccess,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : IAdministrativeDataExportService
{
    /// <inheritdoc/>
    public async Task<PersonalDataExportDetails> RequestAsync(
        Guid administratorId,
        Guid memberId,
        string requestReference,
        CancellationToken cancellationToken)
    {
        AdministrativeDataExportEvent? attemptedAudit = null;
        try
        {

            return await RequestAndAuditAsync(
                administratorId,
                memberId,
                requestReference,
                audit => attemptedAudit = audit,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            if (attemptedAudit is not null)
            {
                var confirmed = await VerifyAuditAsync(
                    attemptedAudit,
                    cancellationToken);

                if (confirmed is not null)
                    return confirmed;
            }

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <inheritdoc/>
    public async Task<PersonalDataExportDetails> GetAsync(
        Guid administratorId,
        Guid memberId,
        Guid? exportId,
        CancellationToken cancellationToken)
    {
        var request = await ReadRequestedAsync(
            administratorId,
            memberId,
            exportId,
            cancellationToken);

        return request.Export.GetDetails(timeProvider
                .GetUtcNow()
                .UtcDateTime);
    }

    /// <inheritdoc/>
    public async Task<PersonalDataExportDownload> OpenArchiveAsync(
        Guid administratorId,
        Guid memberId,
        Guid exportId,
        CancellationToken cancellationToken)
    {
        var request = await ReadRequestedAsync(
            administratorId,
            memberId,
            exportId,
            cancellationToken);

        return await archiveReader.OpenAsync(
            request.Export,
            token => AuditDownloadAsync(
                administratorId,
                memberId,
                exportId,
                token),
            cancellationToken);
    }

    /// <summary>Commits the export and every successful administrative request together.</summary>
    /// <param name="administratorId">The authenticated actor.</param>
    /// <param name="memberId">The account to export.</param>
    /// <param name="requestReference">The validated reference.</param>
    /// <param name="onCommitAttempt">Records the event immediately before attempting commit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created or reused export.</returns>
    private async Task<PersonalDataExportDetails> RequestAndAuditAsync(
        Guid administratorId,
        Guid memberId,
        string requestReference,
        Action<AdministrativeDataExportEvent> onCommitAttempt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await requestRepository.LockAccountsAsync(
            administratorId,
            memberId,
            cancellationToken);
        await EnsureAccessAsync(
            context,
            administratorAccess,
            administratorId,
            memberId,
            cancellationToken);
        var now = timeProvider
            .GetUtcNow()
            .UtcDateTime;
        var export = await requestRepository.GetOrCreateAsync(
            memberId,
            now,
            cancellationToken);
        var audit = new AdministrativeDataExportEvent(
            administratorId,
            memberId,
            export.Id,
            AdministrativeDataExportAction.Requested,
            requestReference,
            now);
        context.AdministrativeDataExportEvents.Add(audit);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        onCommitAttempt(audit);
        await transaction.CommitAsync(cancellationToken);

        return export.GetDetails(now);
    }

    /// <summary>Restricts reads to retained archives with a durable administrative request.</summary>
    /// <param name="administratorId">The authenticated actor.</param>
    /// <param name="memberId">The account to export.</param>
    /// <param name="exportId">The optional export identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The export and its latest administrative reference.</returns>
    private async Task<(MemberDataExport Export, string RequestReference)> ReadRequestedAsync(
        Guid administratorId,
        Guid memberId,
        Guid? exportId,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnsureAccessAsync(
                context,
                administratorAccess,
                administratorId,
                memberId,
                cancellationToken);
            var query = context.AdministrativeDataExportEvents
                .AsNoTracking()
                .Where(audit => audit.MemberId == memberId && audit.Action == AdministrativeDataExportAction.Requested);

            if (exportId.HasValue)
                query = query.Where(audit => audit.ExportId == exportId.Value);
            var request = await query
                .Join(
                context.MemberDataExports
                    .AsNoTracking()
                    .Where(export => export.MemberId == memberId),
                audit => audit.ExportId,
                export => export.Id,
                (
                    audit,
                    export) => new
                    {
                        Export = export,
                        audit.RequestReference,
                        audit.CreatedAt,
                        audit.Id
                    })
                .OrderByDescending(request => request.CreatedAt)
                .ThenByDescending(request => request.Id)
                .FirstOrDefaultAsync(cancellationToken) ?? throw new PersonalDataExportNotFoundException();

            return (request.Export, request.RequestReference);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>Records download intent only after checking current accounts and archive state.</summary>
    /// <param name="administratorId">The authenticated actor.</param>
    /// <param name="memberId">The target account.</param>
    /// <param name="exportId">The export identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing durable release authorization.</returns>
    private async Task AuditDownloadAsync(
        Guid administratorId,
        Guid memberId,
        Guid exportId,
        CancellationToken cancellationToken)
    {
        AdministrativeDataExportEvent? attemptedAudit = null;
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await requestRepository.LockAccountsAsync(
                administratorId,
                memberId,
                cancellationToken);
            var request = await ReadRequestedAsync(
                administratorId,
                memberId,
                exportId,
                cancellationToken);
            var now = timeProvider
                .GetUtcNow()
                .UtcDateTime;

            if (request.Export
                .GetDetails(now)
                .Status is not PersonalDataExportStatus.Ready)
                throw new PersonalDataExportNotFoundException();
            var audit = new AdministrativeDataExportEvent(
                administratorId,
                memberId,
                exportId,
                AdministrativeDataExportAction.DownloadStarted,
                request.RequestReference,
                now);
            context.AdministrativeDataExportEvents.Add(audit);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            attemptedAudit = audit;
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            if (attemptedAudit is not null)
            {
                var confirmed = await VerifyAuditAsync(
                    attemptedAudit,
                    cancellationToken);

                if (confirmed?.Status is PersonalDataExportStatus.Ready)
                    return;
            }

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>Checks the actor's current role and the target's existence without requiring target email confirmation.</summary>
    /// <param name="context">The current verification context.</param>
    /// <param name="administratorAccess">The database-backed role reader.</param>
    /// <param name="administratorId">The authenticated actor.</param>
    /// <param name="memberId">The target account.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing access validation.</returns>
    private static async Task EnsureAccessAsync(
        MonKadoDbContext context,
        IAdministratorAccessService administratorAccess,
        Guid administratorId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var access = await administratorAccess.GetAccessAsync(
            administratorId,
            cancellationToken);

        if (access is AdministratorAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is not AdministratorAccess.Granted)
            throw new AdministratorAccessDeniedException();

        if (!await context.Users
            .AsNoTracking()
            .AnyAsync(
            member => member.Id == administratorId && member.EmailConfirmed,
            cancellationToken))
            throw new InvalidAuthenticationSessionException();

        if (!await context.Users
            .AsNoTracking()
            .AnyAsync(
            member => member.Id == memberId,
            cancellationToken))
            throw new PersonalDataExportNotFoundException();
    }

    /// <summary>Confirms an exact audit commit in an independent context without replaying a request or download.</summary>
    /// <param name="attempted">The exact event submitted for commit.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Current authorized metadata, or null when commit cannot be confirmed.</returns>
    private async Task<PersonalDataExportDetails?> VerifyAuditAsync(
        AdministrativeDataExportEvent attempted,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var verification = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<IAdministrativeDataExportService>();
            await EnsureAccessAsync(
                verification,
                scope.ServiceProvider.GetRequiredService<IAdministratorAccessService>(),
                attempted.AdministratorId.GetValueOrDefault(),
                attempted.MemberId.GetValueOrDefault(),
                cancellationToken);
            var committed = await verification.AdministrativeDataExportEvents
                .AsNoTracking()
                .AnyAsync(
                audit => audit.Id == attempted.Id && audit.ExportId == attempted.ExportId && audit.AdministratorId == attempted.AdministratorId && audit.MemberId == attempted.MemberId && audit.Action == attempted.Action && audit.RequestReference == attempted.RequestReference,
                cancellationToken);

            if (!committed)
                return null;

            return await service.GetAsync(
                attempted.AdministratorId.GetValueOrDefault(),
                attempted.MemberId.GetValueOrDefault(),
                attempted.ExportId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception) || exception is DependencyUnavailableException)
        {

            return null;
        }
    }
}
