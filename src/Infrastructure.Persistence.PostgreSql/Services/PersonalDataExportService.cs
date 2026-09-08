using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Coordinates export requests with account deletion and durable member quotas.</summary>
/// <param name="context">The scoped database context.</param>
/// <param name="unitOfWork">The shared unit of work.</param>
/// <param name="userRepository">The account locking repository.</param>
/// <param name="archiveReader">The private archive reader.</param>
/// <param name="requestRepository">The shared member quota and reuse coordinator.</param>
/// <param name="timeProvider">The UTC clock.</param>
/// <param name="scopeFactory">The independent commit verification scope factory.</param>
public class PersonalDataExportService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IMonKadoUserRepository userRepository,
    IPersonalDataExportArchiveReader archiveReader,
    IPersonalDataExportRequestRepository requestRepository,
    TimeProvider timeProvider,
    IServiceScopeFactory scopeFactory) : IPersonalDataExportService
{
    /// <inheritdoc/>
    public async Task<PersonalDataExportDetails> RequestAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        Guid? committedRequestId = null;
        try
        {

            return await CreateOrReuseAsync(
                memberId,
                requestId => committedRequestId = requestId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            if (committedRequestId is { } requestId)
            {
                var confirmed = await VerifyRequestAsync(
                    memberId,
                    requestId,
                    cancellationToken);

                if (confirmed is not null)
                    return confirmed;
            }

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>Creates or reuses a request under the member lock and releases it before independent commit verification.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="onCommitAttempt">Records the identifier immediately before commit is attempted.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The request lifecycle details.</returns>
    private async Task<PersonalDataExportDetails> CreateOrReuseAsync(
        Guid memberId,
        Action<Guid> onCommitAttempt,
        CancellationToken cancellationToken)
    {
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var member = await userRepository.GetByIdForUpdateAsync(
            memberId,
            cancellationToken);

        if (member is not { EmailConfirmed: true })
            throw new InvalidAuthenticationSessionException();
        var now = timeProvider
            .GetUtcNow()
            .UtcDateTime;
        var request = await requestRepository.GetOrCreateAsync(
            memberId,
            now,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        onCommitAttempt(request.Id);
        await transaction.CommitAsync(cancellationToken);

        return request.GetDetails(now);
    }

    /// <inheritdoc/>
    public async Task<PersonalDataExportDetails> GetAsync(
        Guid memberId,
        Guid? exportId,
        CancellationToken cancellationToken)
    {
        var export = await ReadOwnedAsync(
            memberId,
            exportId,
            cancellationToken);

        return export.GetDetails(timeProvider
                .GetUtcNow()
                .UtcDateTime);
    }

    /// <inheritdoc/>
    public async Task<PersonalDataExportDownload> OpenArchiveAsync(
        Guid memberId,
        Guid exportId,
        CancellationToken cancellationToken)
    {
        var export = await ReadOwnedAsync(
            memberId,
            exportId,
            cancellationToken);

        return await archiveReader.OpenAsync(
            export,
            token => EnsureMemberAsync(
                memberId,
                token),
            cancellationToken);
    }

    /// <summary>Reads a request through an owner predicate, never an administrator override.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="exportId">The requested identifier, or null for the latest request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The no-tracking owned request.</returns>
    private async Task<MemberDataExport> ReadOwnedAsync(
        Guid memberId,
        Guid? exportId,
        CancellationToken cancellationToken)
    {
        try
        {
            await EnsureMemberAsync(
                memberId,
                cancellationToken);
            var query = context.MemberDataExports
                .AsNoTracking()
                .Where(export => export.MemberId == memberId);

            if (exportId.HasValue)
                query = query.Where(export => export.Id == exportId.Value);
            var export = await query
                .OrderByDescending(export => export.CreatedAt)
                .ThenByDescending(export => export.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return export ?? throw new PersonalDataExportNotFoundException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>Rechecks account existence without trusting a still-unexpired JWT alone.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the account check.</returns>
    private async Task EnsureMemberAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        try
        {
            var exists = await context.Users
                .AsNoTracking()
                .AnyAsync(
                member => member.Id == memberId && member.EmailConfirmed,
                cancellationToken);

            if (!exists)
                throw new InvalidAuthenticationSessionException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>Checks an uncertain commit in an independent context without replaying creation.</summary>
    /// <param name="memberId">The requesting member identifier.</param>
    /// <param name="exportId">The attempted durable request identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The confirmed request, or null if its outcome cannot be confirmed.</returns>
    private async Task<PersonalDataExportDetails?> VerifyRequestAsync(
        Guid memberId,
        Guid exportId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var verification = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

            if (!await verification.Users.AnyAsync(
                member => member.Id == memberId && member.EmailConfirmed,
                cancellationToken))
                throw new InvalidAuthenticationSessionException();
            var request = await verification.MemberDataExports
                .AsNoTracking()
                .SingleOrDefaultAsync(
                export => export.Id == exportId && export.MemberId == memberId,
                cancellationToken);

            return request?.GetDetails(timeProvider
                    .GetUtcNow()
                    .UtcDateTime);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            return null;
        }
    }
}
