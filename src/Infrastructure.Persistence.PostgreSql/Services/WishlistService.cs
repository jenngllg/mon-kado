using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates, updates, deletes and retrieves private wishlists in PostgreSQL.
/// </summary>
/// <param name="wishlistRepository">The wishlist repository.</param>
/// <param name="unitOfWork">The unit of work.</param>
/// <param name="timeProvider">The time provider.</param>
public class WishlistService(
    IWishlistRepository wishlistRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IWishlistService
{
    private const string OwnerForeignKeyName = "fk_wishlists_users_owner_id";
    private const string OwnerNormalizedNameIndexName = "ux_wishlists_owner_normalized_name";

    /// <inheritdoc />
    public async Task<WishlistDetails?> CreateAsync(
        Guid id,
        Guid ownerId,
        string name,
        string normalizedName,
        WishlistOccasion occasion,
        DateOnly? eventDate,
        string? message,
        CancellationToken cancellationToken)
    {
        var wishlist = new Wishlist(
            id,
            ownerId,
            name,
            normalizedName,
            occasion,
            eventDate,
            message);
        wishlistRepository.Add(wishlist);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CreateDetails(wishlist);
        }
        catch (DbUpdateException exception) when (IsDuplicateName(exception))
        {
            throw new WishlistNameAlreadyExistsException();
        }
        catch (DbUpdateException exception) when (IsMissingOwner(exception))
        {
            return null;
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            return await ResolveAmbiguousCreationAsync(
                wishlist,
                exception,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<WishlistDetails?> UpdateAsync(
        Guid ownerId,
        Guid wishlistId,
        string name,
        string normalizedName,
        WishlistOccasion occasion,
        DateOnly? eventDate,
        string? message,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        (Wishlist Attempted, Wishlist Original)? attemptedUpdate = null;

        try
        {
            var wishlist = await wishlistRepository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken);

            if (wishlist is null)
                return null;

            if (wishlist.Version != expectedVersion)
                throw new WishlistVersionConflictException();

            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
            var eventDateHasChanged = !Nullable.Equals(
                eventDate,
                wishlist.EventDate);

            if (eventDate is { } requestedEventDate &&
                eventDateHasChanged &&
                requestedEventDate < today)
            {
                throw new RequestValidationException(
                [
                    new ValidationError(
                        "eventDate",
                        ValidationMessages.WishlistEventDateMustBeTodayOrLater)
                ]);
            }

            var originalWishlist = CopyClientState(wishlist);
            var hasChanged = wishlist.Update(
                name,
                normalizedName,
                occasion,
                eventDate,
                message);

            if (!hasChanged)
                return CreateDetails(wishlist);

            attemptedUpdate = (
                wishlist,
                originalWishlist);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CreateDetails(wishlist);
        }
        catch (DbUpdateException exception) when (IsDuplicateName(exception))
        {
            throw new WishlistNameAlreadyExistsException();
        }
        catch (DbUpdateConcurrencyException)
        {
            var access = await GetAccessSafelyAsync(
                ownerId,
                wishlistId,
                cancellationToken);

            if (access is WishlistAccess.MemberNotFound)
                throw new InvalidAuthenticationSessionException();

            if (access is WishlistAccess.NotOwned)
                return null;

            throw new WishlistVersionConflictException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            if (attemptedUpdate is null)
            {
                throw new DependencyUnavailableException(
                    "PostgreSQL",
                    exception);
            }

            return await ResolveAmbiguousUpdateAsync(
                attemptedUpdate.Value.Attempted,
                attemptedUpdate.Value.Original,
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
        var saveAttempted = false;

        try
        {
            var wishlist = await wishlistRepository.GetByIdForUpdateAsync(
                ownerId,
                wishlistId,
                cancellationToken);

            if (wishlist is null)
            {
                var access = await GetAccessSafelyAsync(
                    ownerId,
                    wishlistId,
                    cancellationToken);

                if (access is WishlistAccess.MemberNotFound)
                    throw new InvalidAuthenticationSessionException();

                return false;
            }

            if (wishlist.Version != expectedVersion)
                throw new WishlistVersionConflictException();

            wishlistRepository.Remove(wishlist);
            saveAttempted = true;
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            var access = await GetAccessSafelyAsync(
                ownerId,
                wishlistId,
                cancellationToken);

            if (access is WishlistAccess.MemberNotFound)
                throw new InvalidAuthenticationSessionException();

            if (access is WishlistAccess.NotOwned)
                return false;

            throw new WishlistVersionConflictException();
        }
        catch (Exception exception)
        {
            if (!PostgreSqlFailureClassifier.IsUnavailable(exception))
                throw;

            if (!saveAttempted)
            {
                throw new DependencyUnavailableException(
                    "PostgreSQL",
                    exception);
            }

            return await ResolveAmbiguousDeletionAsync(
                ownerId,
                wishlistId,
                exception,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<WishlistDetails?> GetAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        try
        {
            var wishlist = await wishlistRepository.GetByIdAsync(
                wishlistId,
                cancellationToken);

            return wishlist is null
                ? null
                : CreateDetails(wishlist);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<WishlistDetails>?> GetByOwnerIdAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var wishlists = await wishlistRepository.GetByOwnerIdAsync(
                ownerId,
                cancellationToken);

            return wishlists?
                .Select(CreateDetails)
                .ToArray();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<WishlistAccess> GetAccessAsync(
        Guid memberId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await wishlistRepository.GetAccessAsync(
                memberId,
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
    /// Maps a persisted wishlist to its application model.
    /// </summary>
    /// <param name="wishlist">The persisted wishlist.</param>
    /// <returns>The application wishlist details.</returns>
    private static WishlistDetails CreateDetails(Wishlist wishlist)
    {
        return new WishlistDetails(
            wishlist.Id,
            wishlist.Name,
            wishlist.Occasion,
            wishlist.EventDate,
            wishlist.Message,
            wishlist.CreatedAt,
            wishlist.UpdatedAt,
            wishlist.Version);
    }

    /// <summary>
    /// Resolves whether a wishlist creation committed after its acknowledgement was lost.
    /// </summary>
    /// <param name="attemptedWishlist">The exact wishlist whose save was attempted.</param>
    /// <param name="originalException">The transient save exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The committed wishlist, or <see langword="null" /> when its owner disappeared.</returns>
    /// <exception cref="DependencyUnavailableException">The attempted creation cannot be confirmed.</exception>
    private async Task<WishlistDetails?> ResolveAmbiguousCreationAsync(
        Wishlist attemptedWishlist,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        var currentWishlist = await GetByIdSafelyAsync(
            attemptedWishlist.Id,
            cancellationToken);

        if (currentWishlist is not null &&
            HasSameValues(
                currentWishlist,
                attemptedWishlist))
        {
            return CreateDetails(currentWishlist);
        }

        var access = await GetAccessSafelyAsync(
            attemptedWishlist.OwnerId,
            attemptedWishlist.Id,
            cancellationToken);

        if (access is WishlistAccess.MemberNotFound)
            return null;

        throw new DependencyUnavailableException(
            "PostgreSQL",
            originalException);
    }

    /// <summary>
    /// Resolves whether a wishlist update committed after its acknowledgement was lost.
    /// </summary>
    /// <param name="attemptedWishlist">The exact wishlist state whose save was attempted.</param>
    /// <param name="originalWishlist">The exact wishlist state read before the attempted update.</param>
    /// <param name="originalException">The transient save exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The committed wishlist, or <see langword="null" /> when it disappeared.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member disappeared.</exception>
    /// <exception cref="WishlistVersionConflictException">A different wishlist state was committed.</exception>
    /// <exception cref="DependencyUnavailableException">The attempted update cannot be verified.</exception>
    private async Task<WishlistDetails?> ResolveAmbiguousUpdateAsync(
        Wishlist attemptedWishlist,
        Wishlist originalWishlist,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        var currentWishlist = await GetByIdSafelyAsync(
            attemptedWishlist.Id,
            cancellationToken);

        if (currentWishlist is not null &&
            HasSameValues(
                currentWishlist,
                attemptedWishlist))
        {
            return CreateDetails(currentWishlist);
        }

        if (currentWishlist is not null &&
            HasSameValues(
                currentWishlist,
                originalWishlist))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                originalException);
        }

        var access = await GetAccessSafelyAsync(
            attemptedWishlist.OwnerId,
            attemptedWishlist.Id,
            cancellationToken);

        if (access is WishlistAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is WishlistAccess.NotOwned)
            return null;

        throw new WishlistVersionConflictException();
    }

    /// <summary>
    /// Resolves whether a wishlist deletion committed after its acknowledgement was lost.
    /// </summary>
    /// <param name="ownerId">The owner member identifier.</param>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="originalException">The transient save exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the wishlist is no longer available.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The member disappeared.</exception>
    /// <exception cref="DependencyUnavailableException">The attempted deletion cannot be confirmed.</exception>
    private async Task<bool> ResolveAmbiguousDeletionAsync(
        Guid ownerId,
        Guid wishlistId,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        var access = await GetAccessSafelyAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        if (access is WishlistAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is WishlistAccess.NotOwned)
            return true;

        throw new DependencyUnavailableException(
            "PostgreSQL",
            originalException);
    }

    /// <summary>
    /// Copies the identifying and editable state needed to reconcile an ambiguous update.
    /// </summary>
    /// <param name="wishlist">The wishlist to copy.</param>
    /// <returns>A detached copy of the client-controlled state.</returns>
    private static Wishlist CopyClientState(Wishlist wishlist)
    {
        return new Wishlist(
            wishlist.Id,
            wishlist.OwnerId,
            wishlist.Name,
            wishlist.NormalizedName,
            wishlist.Occasion,
            wishlist.EventDate,
            wishlist.Message);
    }

    /// <summary>
    /// Retrieves a wishlist while translating PostgreSQL unavailability.
    /// </summary>
    /// <param name="wishlistId">The wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The wishlist when found; otherwise, <see langword="null" />.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    private async Task<Wishlist?> GetByIdSafelyAsync(
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await wishlistRepository.GetByIdAsync(
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
    /// <param name="ownerId">The owner member identifier.</param>
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
    /// Determines whether an update violated the owner-scoped normalized-name index.
    /// </summary>
    /// <param name="exception">The database update exception.</param>
    /// <returns><see langword="true" /> for the expected unique-index violation.</returns>
    private static bool IsDuplicateName(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: OwnerNormalizedNameIndexName
        };
    }

    /// <summary>
    /// Determines whether an update violated the wishlist owner foreign key.
    /// </summary>
    /// <param name="exception">The database update exception.</param>
    /// <returns><see langword="true" /> for the expected foreign-key violation.</returns>
    private static bool IsMissingOwner(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation,
            ConstraintName: OwnerForeignKeyName
        };
    }

    /// <summary>
    /// Compares the exact client-controlled state of two wishlists.
    /// </summary>
    /// <param name="first">The first wishlist.</param>
    /// <param name="second">The second wishlist.</param>
    /// <returns><see langword="true" /> when their identifying and editable values match.</returns>
    private static bool HasSameValues(
        Wishlist first,
        Wishlist second)
    {
        var firstValues = (
            first.Id,
            first.OwnerId,
            first.Name,
            first.NormalizedName,
            first.Occasion,
            first.EventDate,
            first.Message);
        var secondValues = (
            second.Id,
            second.OwnerId,
            second.Name,
            second.NormalizedName,
            second.Occasion,
            second.EventDate,
            second.Message);

        return firstValues == secondValues;
    }
}
