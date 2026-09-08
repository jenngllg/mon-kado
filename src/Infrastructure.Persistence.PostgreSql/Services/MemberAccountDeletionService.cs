using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Coordinates durable confirmation and atomic account data removal.</summary>
/// <param name="context">The scoped database context.</param>
/// <param name="unitOfWork">The shared unit of work.</param>
/// <param name="userRepository">The account locking repository.</param>
/// <param name="tokenService">The member-bound token service.</param>
/// <param name="options">The stable confirmation policy.</param>
/// <param name="timeProvider">The UTC clock.</param>
/// <param name="dataRemoval">The shared transactional account data remover.</param>
/// <param name="scopeFactory">The factory for independent commit outcome reads.</param>
public class MemberAccountDeletionService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IMonKadoUserRepository userRepository,
    IMemberAccountDeletionTokenService tokenService,
    IOptions<MemberAccountDeletionOptions> options,
    TimeProvider timeProvider,
    IMemberAccountDataRemovalService dataRemoval,
    IServiceScopeFactory scopeFactory) : IMemberAccountDeletionService
{
    /// <inheritdoc/>
    public async Task RequestAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var member = await userRepository.GetByIdForUpdateAsync(
                memberId,
                cancellationToken);

            if (member is not { EmailConfirmed: true, SecurityStamp: { } stamp })
                throw new InvalidAuthenticationSessionException();
            var now = timeProvider
                .GetUtcNow()
                .UtcDateTime;
            var cutoff = now - options.Value.RequestWindow;
            var count = await context.AuthenticationEmailOutboxMessages.CountAsync(
                message => message.UserId == memberId && message.Kind == AuthenticationEmailKind.AccountDeletionConfirmation && message.CreatedAt > cutoff,
                cancellationToken);

            if (count >= options.Value.MaximumRequests)
                throw new MemberAccountDeletionRateLimitException();
            await context.MemberAccountDeletionRequests
                .Where(request => request.MemberId == memberId)
                .ExecuteDeleteAsync(cancellationToken);
            await context.AuthenticationEmailOutboxMessages
                .Where(message => message.UserId == memberId && message.Kind == AuthenticationEmailKind.AccountDeletionConfirmation && message.ProcessedAt == null)
                .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                    message => message.ProcessedAt,
                    now)
                    .SetProperty(
                    message => message.LockedUntil,
                    (DateTime?)null),
                cancellationToken);
            var request = new MemberAccountDeletionRequest(
                memberId,
                member.Email!, // PostgreSQL requires Email; Identity's base property remains nullable.
                stamp,
                now,
                options.Value.Lifetime);
            context.MemberAccountDeletionRequests.Add(request);
            context.AuthenticationEmailOutboxMessages.Add(AuthenticationEmailOutboxMessage.CreateAccountDeletionConfirmation(request));
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <inheritdoc/>
    public async Task ConfirmAsync(
        Guid memberId,
        string token,
        CancellationToken cancellationToken)
    {
        var requestId = tokenService.Read(
            memberId,
            token);

        if (requestId is null)
            throw new MemberAccountDeletionInvalidException();
        var commitAttempted = false;
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var member = await userRepository.GetByIdForUpdateAsync(
                memberId,
                cancellationToken);

            if (member is null)
                throw new InvalidAuthenticationSessionException();
            var request = await context.MemberAccountDeletionRequests.SingleOrDefaultAsync(
                candidate => candidate.Id == requestId && candidate.MemberId == memberId,
                cancellationToken);

            if (request is null || !member.EmailConfirmed || !request.IsValid(
                member.Email,
                member.SecurityStamp,
                timeProvider
                    .GetUtcNow()
                    .UtcDateTime))
                throw new MemberAccountDeletionInvalidException();
            await dataRemoval.StageAsync(
                member,
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            commitAttempted = true;
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            if (commitAttempted && await IsDeletionConfirmedAsync(
                memberId,
                cancellationToken))
                return;

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>Rechecks durable account absence after an ambiguous commit.</summary>
    /// <param name="memberId">The deleted member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether PostgreSQL confirms that the account no longer exists.</returns>
    private async Task<bool> IsDeletionConfirmedAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var verificationContext = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

            return !await verificationContext.Users
                .AsNoTracking()
                .AnyAsync(
                member => member.Id == memberId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            return false;
        }
    }
}
