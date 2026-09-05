using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using System.Security.Cryptography;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Manages wishlist share links persisted in PostgreSQL.
/// </summary>
/// <param name="shareLinkRepository">The share-link repository.</param>
/// <param name="wishlistRepository">The wishlist repository.</param>
/// <param name="tokenService">The share-token service.</param>
/// <param name="unitOfWork">The unit of work.</param>
public class WishlistShareService(
    IWishlistShareLinkRepository shareLinkRepository,
    IWishlistRepository wishlistRepository,
    IWishlistShareTokenService tokenService,
    IUnitOfWork unitOfWork) : IWishlistShareService
{
    private const string WishlistForeignKeyName = "fk_wishlist_share_links_wishlists_wishlist_id";
    private const string WishlistIndexName = "ux_wishlist_share_links_wishlist_id";

    /// <inheritdoc />
    public async Task<WishlistShareLinkDetails?> CreateAsync(
        Guid id,
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        await EnsureOwnershipAsync(
            ownerId,
            wishlistId,
            cancellationToken);
        var token = tokenService.Create();
        var shareLink = new WishlistShareLink(
            id,
            wishlistId,
            token.SecretHash,
            token.ProtectedSecret);
        shareLinkRepository.Add(shareLink);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CreateDetails(
                shareLink,
                token.Secret);
        }
        catch (DbUpdateException exception) when (IsDuplicateWishlist(exception))
        {
            throw new WishlistShareLinkAlreadyExistsException();
        }
        catch (DbUpdateException exception) when (IsMissingWishlist(exception))
        {
            return null;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            return await ResolveAmbiguousCreationAsync(
                ownerId,
                shareLink,
                token,
                exception,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<WishlistShareLinkDetails?> GetAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        await EnsureOwnershipAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        try
        {
            var shareLink = await shareLinkRepository.GetByWishlistIdAsync(
                wishlistId,
                cancellationToken);

            return shareLink is null
                ? null
                : CreateDetails(
                    shareLink,
                    tokenService.Unprotect(shareLink.ProtectedSecret));
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<WishlistShareLinkDetails?> RotateAsync(
        Guid ownerId,
        Guid wishlistId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        (Guid Id, byte[] OriginalHash, string OriginalProtectedSecret, WishlistShareToken Token)? attemptedRotation = null;

        await EnsureOwnershipAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        try
        {
            var shareLink = await shareLinkRepository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken);

            if (shareLink is null)
                return null;

            if (shareLink.Version != expectedVersion)
                throw new WishlistShareLinkVersionConflictException();

            var token = tokenService.Create();
            attemptedRotation = (
                shareLink.Id,
                shareLink.SecretHash.ToArray(),
                shareLink.ProtectedSecret,
                token);
            shareLink.Rotate(
                token.SecretHash,
                token.ProtectedSecret);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CreateDetails(
                shareLink,
                token.Secret);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolveConcurrentRotationAsync(
                ownerId,
                wishlistId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            if (!PostgreSqlFailureClassifier.IsUnavailable(exception))
                throw;

            if (attemptedRotation is null)
            {
                throw new DependencyUnavailableException(
                    "PostgreSQL",
                    exception);
            }

            return await ResolveAmbiguousRotationAsync(
                ownerId,
                wishlistId,
                attemptedRotation.Value,
                exception,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(
        Guid ownerId,
        Guid wishlistId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        Guid? attemptedShareLinkId = null;

        await EnsureOwnershipAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        try
        {
            var shareLink = await shareLinkRepository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken);

            if (shareLink is null)
                return false;

            if (shareLink.Version != expectedVersion)
                throw new WishlistShareLinkVersionConflictException();

            attemptedShareLinkId = shareLink.Id;
            shareLinkRepository.Remove(shareLink);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolveConcurrentDeletionAsync(
                ownerId,
                wishlistId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            if (!PostgreSqlFailureClassifier.IsUnavailable(exception))
                throw;

            if (attemptedShareLinkId is null)
            {
                throw new DependencyUnavailableException(
                    "PostgreSQL",
                    exception);
            }

            return await ResolveAmbiguousDeletionAsync(
                ownerId,
                wishlistId,
                attemptedShareLinkId.Value,
                exception,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<SharedWishlistDetails?> GetSharedAsync(
        Guid shareLinkId,
        string secret,
        CancellationToken cancellationToken)
    {
        try
        {
            var shareLink = await GetVerifiedShareLinkAsync(
                shareLinkId,
                secret,
                cancellationToken);

            if (shareLink is null)
                return null;

            return await shareLinkRepository.GetSharedWishlistAsync(
                shareLink.WishlistId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<SharedWishLookupResult> GetSharedWishAsync(
        Guid shareLinkId,
        string secret,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        try
        {
            var shareLink = await GetVerifiedShareLinkAsync(
                shareLinkId,
                secret,
                cancellationToken);

            if (shareLink is null)
            {
                return new SharedWishLookupResult(
                    SharedWishLookupOutcome.SharedWishlistNotFound,
                    null,
                    null);
            }

            var wish = await shareLinkRepository.GetSharedWishAsync(
                shareLink.WishlistId,
                wishId,
                cancellationToken);

            return new SharedWishLookupResult(
                wish is null
                    ? SharedWishLookupOutcome.WishNotFound
                    : SharedWishLookupOutcome.Found,
                shareLink.WishlistId,
                wish);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>Gets a share link only when the presented secret matches it.</summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="secret">The presented secret.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The verified share link, or <see langword="null" />.</returns>
    private async Task<WishlistShareLink?> GetVerifiedShareLinkAsync(
        Guid shareLinkId,
        string secret,
        CancellationToken cancellationToken)
    {
        var shareLink = await shareLinkRepository.GetByIdAsync(
            shareLinkId,
            cancellationToken);

        if (shareLink is null)
            return null;

        if (!tokenService.Verify(
            secret,
            shareLink.SecretHash))
            return null;

        return shareLink;
    }

    /// <summary>
    /// Resolves whether a share-link creation committed after its acknowledgement was lost.
    /// </summary>
    /// <param name="ownerId">The owner identifier.</param>
    /// <param name="attemptedShareLink">The exact share link whose save was attempted.</param>
    /// <param name="token">The generated token material.</param>
    /// <param name="originalException">The transient save exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The committed link, or <see langword="null" /> when the wishlist disappeared.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member disappeared.</exception>
    /// <exception cref="DependencyUnavailableException">The creation cannot be confirmed.</exception>
    private async Task<WishlistShareLinkDetails?> ResolveAmbiguousCreationAsync(
        Guid ownerId,
        WishlistShareLink attemptedShareLink,
        WishlistShareToken token,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        var currentShareLink = await GetByIdSafelyAsync(
            attemptedShareLink.Id,
            cancellationToken);

        if (currentShareLink is not null &&
            currentShareLink.WishlistId == attemptedShareLink.WishlistId &&
            HasSameSecret(
                currentShareLink,
                attemptedShareLink.SecretHash,
                attemptedShareLink.ProtectedSecret))
        {
            return CreateDetails(
                currentShareLink,
                token.Secret);
        }

        var access = await GetAccessSafelyAsync(
            ownerId,
            attemptedShareLink.WishlistId,
            cancellationToken);

        if (access is WishlistAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is not WishlistAccess.Owner)
            return null;

        throw new DependencyUnavailableException(
            "PostgreSQL",
            originalException);
    }

    /// <summary>
    /// Resolves whether a share-link rotation committed after its acknowledgement was lost.
    /// </summary>
    /// <param name="ownerId">The owner identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="attemptedRotation">The exact rotation state.</param>
    /// <param name="originalException">The transient save exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The committed link, or <see langword="null" /> when it disappeared.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member disappeared.</exception>
    /// <exception cref="WishlistShareLinkVersionConflictException">A different state was committed.</exception>
    /// <exception cref="DependencyUnavailableException">The rotation cannot be confirmed.</exception>
    private async Task<WishlistShareLinkDetails?> ResolveAmbiguousRotationAsync(
        Guid ownerId,
        Guid wishlistId,
        (Guid Id, byte[] OriginalHash, string OriginalProtectedSecret, WishlistShareToken Token) attemptedRotation,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        var currentShareLink = await GetByIdSafelyAsync(
            attemptedRotation.Id,
            cancellationToken);

        if (currentShareLink is not null &&
            HasSameSecret(
                currentShareLink,
                attemptedRotation.Token.SecretHash,
                attemptedRotation.Token.ProtectedSecret))
        {
            return CreateDetails(
                currentShareLink,
                attemptedRotation.Token.Secret);
        }

        if (currentShareLink is not null &&
            HasSameSecret(
                currentShareLink,
                attemptedRotation.OriginalHash,
                attemptedRotation.OriginalProtectedSecret))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                originalException);
        }

        var access = await GetAccessSafelyAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        if (access is WishlistAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is not WishlistAccess.Owner || currentShareLink is null)
            return null;

        throw new WishlistShareLinkVersionConflictException();
    }

    /// <summary>
    /// Resolves a concurrency failure while rotating a share link.
    /// </summary>
    /// <param name="ownerId">The owner identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="null" /> when the link disappeared.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member disappeared.</exception>
    /// <exception cref="WishlistShareLinkVersionConflictException">The link still exists with another version.</exception>
    private async Task<WishlistShareLinkDetails?> ResolveConcurrentRotationAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var access = await GetAccessSafelyAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        if (access is WishlistAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is not WishlistAccess.Owner)
            return null;

        var currentShareLink = await GetByWishlistIdSafelyAsync(
            wishlistId,
            cancellationToken);

        if (currentShareLink is null)
            return null;

        throw new WishlistShareLinkVersionConflictException();
    }

    /// <summary>
    /// Resolves whether a share-link deletion committed after its acknowledgement was lost.
    /// </summary>
    /// <param name="ownerId">The owner identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="attemptedShareLinkId">The deleted share-link identifier.</param>
    /// <param name="originalException">The transient save exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the link no longer exists.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member disappeared.</exception>
    /// <exception cref="WishlistShareLinkVersionConflictException">A replacement link exists.</exception>
    /// <exception cref="DependencyUnavailableException">The deletion cannot be confirmed.</exception>
    private async Task<bool> ResolveAmbiguousDeletionAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid attemptedShareLinkId,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        var access = await GetAccessSafelyAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        if (access is WishlistAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is not WishlistAccess.Owner)
            return true;

        var currentShareLink = await GetByWishlistIdSafelyAsync(
            wishlistId,
            cancellationToken);

        if (currentShareLink is null)
            return true;

        if (currentShareLink.Id != attemptedShareLinkId)
            throw new WishlistShareLinkVersionConflictException();

        throw new DependencyUnavailableException(
            "PostgreSQL",
            originalException);
    }

    /// <summary>
    /// Resolves a concurrency failure while deleting a share link.
    /// </summary>
    /// <param name="ownerId">The owner identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="false" /> when the link disappeared.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member disappeared.</exception>
    /// <exception cref="WishlistShareLinkVersionConflictException">The link still exists.</exception>
    private async Task<bool> ResolveConcurrentDeletionAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var access = await GetAccessSafelyAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        if (access is WishlistAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is not WishlistAccess.Owner)
            return false;

        var currentShareLink = await GetByWishlistIdSafelyAsync(
            wishlistId,
            cancellationToken);

        if (currentShareLink is null)
            return false;

        throw new WishlistShareLinkVersionConflictException();
    }

    /// <summary>
    /// Verifies that a wishlist belongs to the authenticated member.
    /// </summary>
    /// <param name="ownerId">The owner identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the operation.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member disappeared.</exception>
    /// <exception cref="WishlistNotFoundException">The wishlist is not owned by the member.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    private async Task EnsureOwnershipAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        var access = await GetAccessSafelyAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        if (access is WishlistAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is not WishlistAccess.Owner)
            throw new WishlistNotFoundException();
    }

    /// <summary>
    /// Retrieves a share link by identifier while translating PostgreSQL unavailability.
    /// </summary>
    /// <param name="shareLinkId">The share-link identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The share link, or <see langword="null" />.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    private async Task<WishlistShareLink?> GetByIdSafelyAsync(
        Guid shareLinkId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await shareLinkRepository.GetByIdAsync(
                shareLinkId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>
    /// Retrieves a share link by wishlist while translating PostgreSQL unavailability.
    /// </summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The share link, or <see langword="null" />.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    private async Task<WishlistShareLink?> GetByWishlistIdSafelyAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await shareLinkRepository.GetByWishlistIdAsync(
                wishlistId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>
    /// Retrieves wishlist access while translating PostgreSQL unavailability.
    /// </summary>
    /// <param name="ownerId">The owner identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current access state.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    private async Task<WishlistAccess> GetAccessSafelyAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await wishlistRepository.GetAccessAsync(
                ownerId,
                wishlistId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <summary>
    /// Maps a persisted share link to its application model.
    /// </summary>
    /// <param name="shareLink">The persisted share link.</param>
    /// <param name="secret">The unprotected bearer secret.</param>
    /// <returns>The application share-link details.</returns>
    private static WishlistShareLinkDetails CreateDetails(
        WishlistShareLink shareLink,
        string secret)
    {
        return new WishlistShareLinkDetails(
            shareLink.Id,
            shareLink.WishlistId,
            secret,
            shareLink.CreatedAt,
            shareLink.UpdatedAt,
            shareLink.Version);
    }

    /// <summary>
    /// Determines whether an update violated the one-link-per-wishlist index.
    /// </summary>
    /// <param name="exception">The database update exception.</param>
    /// <returns><see langword="true" /> for the expected unique-index violation.</returns>
    private static bool IsDuplicateWishlist(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: WishlistIndexName
        };
    }

    /// <summary>
    /// Determines whether the referenced wishlist disappeared during creation.
    /// </summary>
    /// <param name="exception">The database update exception.</param>
    /// <returns><see langword="true" /> for the expected foreign-key violation.</returns>
    private static bool IsMissingWishlist(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation,
            ConstraintName: WishlistForeignKeyName
        };
    }

    /// <summary>
    /// Compares the exact secret state of a share link.
    /// </summary>
    /// <param name="shareLink">The persisted share link.</param>
    /// <param name="secretHash">The expected hash.</param>
    /// <param name="protectedSecret">The expected protected secret.</param>
    /// <returns><see langword="true" /> when both secret representations match.</returns>
    private static bool HasSameSecret(
        WishlistShareLink shareLink,
        byte[] secretHash,
        string protectedSecret)
    {
        return CryptographicOperations.FixedTimeEquals(
                shareLink.SecretHash,
                secretHash) &&
            shareLink.ProtectedSecret == protectedSecret;
    }
}
