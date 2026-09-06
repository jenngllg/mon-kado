using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Updates profile-photo references and their deletion outbox atomically.</summary>
/// <param name="context">The scoped database context.</param>
/// <param name="unitOfWork">The shared unit of work.</param>
/// <param name="userRepository">The account locking repository.</param>
/// <param name="timeProvider">The UTC clock.</param>
/// <param name="scopeFactory">The factory for independent commit verification reads.</param>
public class ProfileImageService(
    MonKadoDbContext context,
    IUnitOfWork unitOfWork,
    IMonKadoUserRepository userRepository,
    TimeProvider timeProvider,
    IServiceScopeFactory scopeFactory) : IProfileImageService
{
    /// <inheritdoc/>
    public Task<MemberProfile> UpsertAsync(
        Guid memberId,
        Guid imageId,
        byte[] contentHash,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {

        return ChangeAsync(
            memberId,
            imageId,
            contentHash,
            expectedVersion,
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<MemberProfile> DeleteAsync(
        Guid memberId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {

        return ChangeAsync(
            memberId,
            null,
            [],
            expectedVersion,
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> IsCurrentAsync(
        Guid memberId,
        Guid imageId,
        CancellationToken cancellationToken)
    {
        try
        {

            return await context.Users
                .AsNoTracking()
                .AnyAsync(
                member => member.Id == memberId && member.EmailConfirmed && member.ProfileImageId == imageId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>Serializes photo changes with account deletion and records one durable mutation.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="imageId">The replacement identifier, or null for removal.</param>
    /// <param name="contentHash">The normalized hash, or an empty array for removal.</param>
    /// <param name="expectedVersion">The expected account version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The profile confirmed by PostgreSQL.</returns>
    private async Task<MemberProfile> ChangeAsync(
        Guid memberId,
        Guid? imageId,
        byte[] contentHash,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        var commitAttempted = false;
        MemberProfile profile;
        try
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            var member = await userRepository.GetByIdForUpdateAsync(
                memberId,
                cancellationToken);

            if (member is not { EmailConfirmed: true })
                throw new InvalidAuthenticationSessionException();

            if (member.Version != expectedVersion)
                throw new MemberProfileVersionConflictException();

            if (imageId is null && member.ProfileImageId is null)
                throw new ProfileImageNotFoundException();

            if (imageId.HasValue && member.ProfileImageHash is { } currentHash && currentHash
                .AsSpan()
                .SequenceEqual(contentHash))
                return CreateProfile(member);

            if (member.ProfileImageId is { } previousImageId)
            {
                context.GiftImageDeletionOutboxMessages.Add(GiftImageDeletionOutboxMessage.Create(
                        previousImageId,
                        timeProvider
                            .GetUtcNow()
                            .UtcDateTime));
            }

            if (imageId is { } replacementImageId)
            {
                member.SetProfileImage(
                    replacementImageId,
                    contentHash);
            }
            else
            {
                member.RemoveProfileImage();
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            commitAttempted = true;
            await transaction.CommitAsync(cancellationToken);

            profile = CreateProfile(member);
        }
        catch (DbUpdateConcurrencyException)
        {
            await ReadIndependentAsync(
                memberId,
                cancellationToken);

            throw new MemberProfileVersionConflictException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            if (commitAttempted)
            {
                var current = await ReadIndependentAsync(
                    memberId,
                    cancellationToken);

                if (current.ProfileImageId == imageId)
                    return current;
            }

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }

        return profile;
    }

    /// <summary>Reads the durable account through a fresh context after an uncertain outcome.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current profile, without reusing tracked mutation state.</returns>
    private async Task<MemberProfile> ReadIndependentAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        MemberProfile profile;
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var verificationContext = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var member = await verificationContext.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(
                member => member.Id == memberId,
                cancellationToken);

            if (member is not { EmailConfirmed: true })
                throw new InvalidAuthenticationSessionException();

            profile = CreateProfile(member);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }

        return profile;
    }

    /// <summary>Projects the account's current editable profile.</summary>
    /// <param name="member">The persisted account.</param>
    /// <returns>The profile and concurrency version.</returns>
    private static MemberProfile CreateProfile(MonKadoUser member)
    {

        return new MemberProfile(
            member.DisplayName,
            member.Version)
        {
            ProfileImageId = member.ProfileImageId
        };
    }
}
