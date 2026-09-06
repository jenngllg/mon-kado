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
/// <param name="scopeFactory">The factory for independent commit outcome reads.</param>
public class MemberAccountDeletionService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IMonKadoUserRepository userRepository,
    IMemberAccountDeletionTokenService tokenService,
    IOptions<MemberAccountDeletionOptions> options,
    TimeProvider timeProvider,
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
            await RemoveMemberDataAsync(
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

    /// <summary>Locks affected parents before queuing images and removing dependent data.</summary>
    /// <param name="member">The exclusively locked member.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the staged and transactional removal.</returns>
    private async Task RemoveMemberDataAsync(
        MonKadoUser member,
        CancellationToken cancellationToken)
    {
        var wishlists = await context.Wishlists
            .FromSqlInterpolated($"""
            SELECT w.*, w.xmin FROM public.wishlists w
            WHERE w.owner_id = {member.Id} OR EXISTS (
                    SELECT 1 FROM public.wishlist_participants p
            WHERE p.wishlist_id = w.id AND p.member_id = {member.Id})
            ORDER BY w.id FOR UPDATE OF w
        """)
            .ToListAsync(cancellationToken);
        var ownedIds = wishlists
            .Where(wishlist => wishlist.OwnerId == member.Id)
            .Select(wishlist => wishlist.Id)
            .ToArray();
        var wishes = await context.Wishes
            .FromSqlInterpolated($"""
            SELECT w.*, w.xmin FROM public.wishes w
            WHERE w.wishlist_id = ANY({ownedIds})
            ORDER BY w.wishlist_id, w.id FOR UPDATE OF w
        """)
            .ToListAsync(cancellationToken);
        foreach (var imageId in wishes
            .Where(wish => wish.ImageId.HasValue)
            .Select(wish => wish.ImageId.GetValueOrDefault()))
        {
            context.GiftImageDeletionOutboxMessages.Add(GiftImageDeletionOutboxMessage.Create(
                    imageId,
                    timeProvider
                        .GetUtcNow()
                        .UtcDateTime));
        }

        await context.GiftReservationHistories
            .Where(history => ownedIds.Contains(history.WishlistId) && history.MemberId != member.Id)
            .ExecuteUpdateAsync(
            setters => setters
                .SetProperty(
                history => history.WishlistName,
                "Deleted wishlist")
                .SetProperty(
                history => history.WishName,
                "Deleted gift"),
            cancellationToken);
        await context.WishlistParticipants
            .Where(participant => participant.MemberId == member.Id)
            .ExecuteDeleteAsync(cancellationToken);
        context.Wishlists.RemoveRange(wishlists.Where(wishlist => wishlist.OwnerId == member.Id));
        context.Users.Remove(member);
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
