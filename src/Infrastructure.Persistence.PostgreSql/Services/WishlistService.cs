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
/// Creates, updates and retrieves private wishlists in PostgreSQL.
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
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
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

            var hasChanged = wishlist.Update(
                name,
                normalizedName,
                occasion,
                eventDate,
                message);

            if (!hasChanged)
                return CreateDetails(wishlist);

            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CreateDetails(wishlist);
        }
        catch (DbUpdateException exception) when (IsDuplicateName(exception))
        {
            throw new WishlistNameAlreadyExistsException();
        }
        catch (DbUpdateConcurrencyException)
        {

            try
            {
                var access = await wishlistRepository.GetAccessAsync(
                    ownerId,
                    wishlistId,
                    cancellationToken);

                if (access is WishlistAccess.MemberNotFound)
                    throw new InvalidAuthenticationSessionException();

                if (access is WishlistAccess.NotOwned)
                    return null;
            }
            catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
            {
                throw new DependencyUnavailableException(
                    "PostgreSQL",
                    exception);
            }

            throw new WishlistVersionConflictException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
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

    private static bool IsDuplicateName(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: OwnerNormalizedNameIndexName
        };
    }

    private static bool IsMissingOwner(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation,
            ConstraintName: OwnerForeignKeyName
        };
    }
}
