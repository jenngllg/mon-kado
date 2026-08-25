using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates and retrieves gift wishes in PostgreSQL.
/// </summary>
/// <param name="wishRepository">The wish repository.</param>
/// <param name="wishlistRepository">The wishlist repository.</param>
/// <param name="unitOfWork">The unit of work.</param>
public class WishService(
    IWishRepository wishRepository,
    IWishlistRepository wishlistRepository,
    IUnitOfWork unitOfWork) : IWishService
{
    private const string WishlistForeignKeyName = "fk_wishes_wishlists_wishlist_id";
    private const string PositionWishlistForeignKeyName = "fk_wish_position_sequences_wishlists_wishlist_id";

    /// <inheritdoc />
    public async Task<WishDetails?> CreateAsync(
        Guid id,
        Guid ownerId,
        Guid wishlistId,
        string name,
        string? note,
        string? url,
        decimal? price,
        CancellationToken cancellationToken)
    {
        Wish? attemptedWish = null;

        try
        {
            var position = await wishRepository.AllocatePositionAsync(
                wishlistId,
                cancellationToken);
            var wish = new Wish(
                id,
                wishlistId,
                name,
                note,
                url,
                price,
                position);
            attemptedWish = wish;
            wishRepository.Add(wish);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CreateDetails(wish);
        }
        catch (Exception exception) when (IsMissingWishlist(exception))
        {
            return await ResolveMissingWishlistAsync(
                ownerId,
                wishlistId,
                cancellationToken);
        }
        catch (Exception exception)
        {

            if (!PostgreSqlFailureClassifier.IsUnavailable(exception))
                throw;

            if (attemptedWish is null)
            {
                throw new DependencyUnavailableException(
                    "PostgreSQL",
                    exception);
            }

            return await ResolveAmbiguousCreationAsync(
                ownerId,
                attemptedWish,
                exception,
                cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<WishDetails?> GetAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        var wish = await GetByIdSafelyAsync(
            wishlistId,
            wishId,
            cancellationToken);

        return wish is null
            ? null
            : CreateDetails(wish);
    }

    /// <summary>
    /// Resolves whether a gift wish creation committed after its acknowledgement was lost.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="attemptedWish">The exact wish whose save was attempted.</param>
    /// <param name="originalException">The transient save exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The committed wish, or <see langword="null" /> when its parent is unavailable.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member disappeared.</exception>
    /// <exception cref="DependencyUnavailableException">The attempted creation cannot be confirmed.</exception>
    private async Task<WishDetails?> ResolveAmbiguousCreationAsync(
        Guid ownerId,
        Wish attemptedWish,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        var currentWish = await GetByIdSafelyAsync(
            attemptedWish.WishlistId,
            attemptedWish.Id,
            cancellationToken);

        if (currentWish is not null &&
            HasSameClientValues(
                currentWish,
                attemptedWish))
        {
            return CreateDetails(currentWish);
        }

        var access = await GetAccessSafelyAsync(
            ownerId,
            attemptedWish.WishlistId,
            cancellationToken);

        if (access is WishlistAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is WishlistAccess.NotOwned)
            return null;

        throw new DependencyUnavailableException(
            "PostgreSQL",
            originalException);
    }

    /// <summary>
    /// Resolves a parent foreign-key failure without revealing private wishlist existence.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="null" /> when the parent is unavailable.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member disappeared.</exception>
    private async Task<WishDetails?> ResolveMissingWishlistAsync(
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

        return null;
    }

    /// <summary>
    /// Retrieves a gift wish while translating PostgreSQL unavailability.
    /// </summary>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The wish when found; otherwise, <see langword="null" />.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    private async Task<Wish?> GetByIdSafelyAsync(
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await wishRepository.GetByIdAsync(
                wishlistId,
                wishId,
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
    /// Retrieves parent wishlist access while translating PostgreSQL unavailability.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
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
    /// Maps a persisted gift wish to its application model.
    /// </summary>
    /// <param name="wish">The persisted wish.</param>
    /// <returns>The application wish details.</returns>
    private static WishDetails CreateDetails(Wish wish)
    {
        return new WishDetails(
            wish.Id,
            wish.WishlistId,
            wish.Name,
            wish.Note,
            wish.Url,
            wish.Price,
            wish.Position,
            wish.CreatedAt,
            wish.UpdatedAt,
            wish.Version);
    }

    /// <summary>
    /// Determines whether an update violated the wish parent foreign key.
    /// </summary>
    /// <param name="exception">The database update exception.</param>
    /// <returns><see langword="true" /> for the expected foreign-key violation.</returns>
    private static bool IsMissingWishlist(Exception exception)
    {
        var postgresException = exception switch
        {
            PostgresException directException => directException,
            DbUpdateException { InnerException: PostgresException innerException } => innerException,
            _ => null
        };

        return postgresException is
        {
            SqlState: PostgresErrorCodes.ForeignKeyViolation,
            ConstraintName: WishlistForeignKeyName or PositionWishlistForeignKeyName
        };
    }

    /// <summary>
    /// Compares the identifying and client-controlled state of two gift wishes.
    /// </summary>
    /// <param name="first">The first wish.</param>
    /// <param name="second">The second wish.</param>
    /// <returns><see langword="true" /> when their client-controlled values match.</returns>
    private static bool HasSameClientValues(
        Wish first,
        Wish second)
    {
        var firstValues = (
            first.Id,
            first.WishlistId,
            first.Name,
            first.Note,
            first.Url,
            first.Price);
        var secondValues = (
            second.Id,
            second.WishlistId,
            second.Name,
            second.Note,
            second.Url,
            second.Price);

        return firstValues == secondValues;
    }
}
