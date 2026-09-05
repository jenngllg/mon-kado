using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore;

using Npgsql;

using System.Data;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates, retrieves, updates, and deletes gift wishes in PostgreSQL.
/// </summary>
/// <param name="wishRepository">The wish repository.</param>
/// <param name="wishlistRepository">The wishlist repository.</param>
/// <param name="unitOfWork">The unit of work.</param>
/// <param name="wishTransactionFactory">The gift wish transaction factory.</param>
public class WishService(
    IWishRepository wishRepository,
    IWishlistRepository wishlistRepository,
    IUnitOfWork unitOfWork,
    IWishTransactionFactory wishTransactionFactory) : IWishService
{
    private const string WishlistForeignKeyName = "fk_wishes_wishlists_wishlist_id";
    private const string PositionWishlistForeignKeyName = "fk_wish_position_sequences_wishlists_wishlist_id";
    private const string WishCountConstraintName = "ck_wish_position_sequences_current_count_limit";
    private const string WishQuantityConstraintName = "ck_wishes_quantity_not_below_reserved";

    /// <inheritdoc />
    public async Task<WishCollectionDetails> GetCollectionAsync(
        Guid ownerId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
        await EnsureOwnedWishlistAsync(
            ownerId,
            wishlistId,
            cancellationToken);
        WishCollectionDetails result;

        try
        {
            await using var transaction = await wishTransactionFactory.BeginAsync(
                IsolationLevel.RepeatableRead,
                cancellationToken);
            var sequence = await wishRepository.GetCollectionStateAsync(
                wishlistId,
                cancellationToken);

            if (sequence is null)
                throw new WishlistNotFoundException();

            var wishes = await wishRepository.GetByWishlistIdAsync(
                wishlistId,
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            result = CreateCollectionDetails(
                wishes,
                sequence.Version);
        }
        catch (Exception exception)
            when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<WishOrderDetails> ReorderAsync(
        Guid ownerId,
        Guid wishlistId,
        IReadOnlyCollection<Guid> wishIds,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        await EnsureOwnedWishlistAsync(
            ownerId,
            wishlistId,
            cancellationToken);
        IReadOnlyCollection<Guid> originalOrder = [];
        WishOrderDetails result;
        var commitAttempted = false;

        try
        {
            await using var transaction = await wishTransactionFactory.BeginAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken);
            // Lock wishes first to match update and delete trigger lock ordering.
            var wishes = await wishRepository.GetByWishlistIdForUpdateAsync(
                wishlistId,
                cancellationToken);
            var sequence = await wishRepository.GetCollectionStateForUpdateAsync(
                wishlistId,
                cancellationToken);

            if (sequence is null)
                throw new WishlistNotFoundException();

            if (sequence.Version != expectedVersion)
                throw new WishOrderVersionConflictException();
            originalOrder = wishes
                .Select(wish => wish.Id)
                .ToArray();

            if (!HasExactMembership(
                wishes,
                wishIds))
            {
                throw new WishOrderConflictException();
            }

            result = CreateOrderDetails(
                wishes,
                sequence.Version);

            if (!originalOrder.SequenceEqual(wishIds))
            {
                var positions = wishes
                    .Select(wish => wish.Position)
                    .Order()
                    .ToArray();
                var wishesById = wishes.ToDictionary(wish => wish.Id);
                var finalPositions = wishIds
                    .Select((
                        wishId,
                        index) => new
                        {
                            Wish = wishesById[wishId],
                            Position = positions[index]
                        })
                    .Where(item => item.Wish.Position != item.Position)
                    .ToArray();
                var temporaryPosition = checked(positions[^1] + 1);

                foreach (var item in finalPositions)
                {
                    item.Wish.MoveTo(temporaryPosition);
                    temporaryPosition = checked(temporaryPosition + 1);
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);

                foreach (var item in finalPositions)
                    item.Wish.MoveTo(item.Position);

                await unitOfWork.SaveChangesAsync(cancellationToken);
                await wishRepository.ReloadCollectionStateAsync(
                    sequence,
                    cancellationToken);
                result = CreateOrderDetails(
                    wishIds
                        .Select(wishId => wishesById[wishId])
                        .ToArray(),
                    sequence.Version);
                commitAttempted = true;
                await transaction.CommitAsync(cancellationToken);
            }
        }
        catch (Exception exception)
            when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            wishRepository.ClearTracking();

            if (!commitAttempted)
            {
                throw new DependencyUnavailableException(
                    "PostgreSQL",
                    exception);
            }

            return await ResolveAmbiguousReorderAsync(
                ownerId,
                wishlistId,
                originalOrder,
                wishIds,
                exception,
                cancellationToken);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<WishDetails?> CreateAsync(
        Guid id,
        Guid ownerId,
        Guid wishlistId,
        string name,
        string? note,
        string? url,
        decimal? price,
        int quantity,
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
                position,
                quantity);
            attemptedWish = wish;
            wishRepository.Add(wish);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CreateDetails(wish);
        }
        catch (Exception exception) when (IsWishLimitReached(exception))
        {
            throw new WishLimitReachedException();
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

    /// <summary>
    /// Resolves whether a collection reorder committed after its acknowledgement was lost.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="originalOrder">The exact order before the attempted reorder.</param>
    /// <param name="requestedOrder">The exact requested final order.</param>
    /// <param name="originalException">The transient commit exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The committed order when it can be confirmed.</returns>
    /// <exception cref="DependencyUnavailableException">The original order is still present.</exception>
    /// <exception cref="WishOrderVersionConflictException">A third state was committed.</exception>
    private async Task<WishOrderDetails> ResolveAmbiguousReorderAsync(
        Guid ownerId,
        Guid wishlistId,
        IReadOnlyCollection<Guid> originalOrder,
        IReadOnlyCollection<Guid> requestedOrder,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        var collection = await GetCollectionAsync(
            ownerId,
            wishlistId,
            cancellationToken);
        var currentOrder = collection.Wishes
            .Select(wish => wish.Id)
            .ToArray();

        if (currentOrder.SequenceEqual(requestedOrder))
            return CreateOrderDetails(
                collection.Wishes,
                collection.Version);

        if (currentOrder.SequenceEqual(originalOrder))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                originalException);
        }

        throw new WishOrderVersionConflictException();
    }

    /// <summary>
    /// Ensures that the current member owns the requested private wishlist.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous access check.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member disappeared.</exception>
    /// <exception cref="WishlistNotFoundException">The parent is unavailable to the owner.</exception>
    private async Task EnsureOwnedWishlistAsync(
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

        if (access is WishlistAccess.NotOwned)
            throw new WishlistNotFoundException();
    }

    /// <summary>
    /// Determines whether a requested order contains every current wish exactly once.
    /// </summary>
    /// <param name="wishes">The current persisted collection.</param>
    /// <param name="wishIds">The requested identifiers.</param>
    /// <returns><see langword="true" /> when both collections have exact membership.</returns>
    private static bool HasExactMembership(
        IReadOnlyCollection<Wish> wishes,
        IReadOnlyCollection<Guid> wishIds)
    {
        if (wishes.Count != wishIds.Count)
            return false;

        var currentIds = wishes
            .Select(wish => wish.Id)
            .ToHashSet();

        return currentIds.SetEquals(wishIds);
    }

    /// <summary>
    /// Maps persisted wishes to a complete versioned collection.
    /// </summary>
    /// <param name="wishes">The persisted wishes.</param>
    /// <param name="version">The collection version.</param>
    /// <returns>The complete application collection.</returns>
    private static WishCollectionDetails CreateCollectionDetails(
        IEnumerable<Wish> wishes,
        uint version)
    {
        var details = wishes
            .Select(CreateDetails)
            .ToArray();

        return new WishCollectionDetails(
            details,
            version);
    }

    /// <summary>
    /// Maps persisted wishes to a complete lightweight order.
    /// </summary>
    /// <param name="wishes">The ordered persisted wishes.</param>
    /// <param name="version">The collection version.</param>
    /// <returns>The complete application order.</returns>
    private static WishOrderDetails CreateOrderDetails(
        IEnumerable<Wish> wishes,
        uint version)
    {
        var items = wishes
            .Select(wish => new WishOrderItem(
                wish.Id,
                wish.Position,
                wish.Version))
            .ToArray();

        return new WishOrderDetails(
            items,
            version);
    }

    /// <summary>
    /// Maps application wish details to a complete lightweight order.
    /// </summary>
    /// <param name="wishes">The ordered application wishes.</param>
    /// <param name="version">The collection version.</param>
    /// <returns>The complete application order.</returns>
    private static WishOrderDetails CreateOrderDetails(
        IEnumerable<WishDetails> wishes,
        uint version)
    {
        var items = wishes
            .Select(wish => new WishOrderItem(
                wish.Id,
                wish.Position,
                wish.Version))
            .ToArray();

        return new WishOrderDetails(
            items,
            version);
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

    /// <inheritdoc />
    public async Task<WishDetails?> UpdateAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        string name,
        string? note,
        string? url,
        decimal? price,
        int quantity,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        (Wish Attempted, Wish Original)? attemptedUpdate = null;

        try
        {
            var wish = await wishRepository.GetByIdForUpdateAsync(
                wishlistId,
                wishId,
                cancellationToken);

            if (wish is null)
                return await ResolveMissingWishAsync(
                    ownerId,
                    wishlistId,
                    cancellationToken);

            if (wish.Version != expectedVersion)
                throw new WishVersionConflictException();

            var originalWish = CopyClientState(wish);
            var hasChanged = wish.Update(
                name,
                note,
                url,
                price,
                quantity);

            if (!hasChanged)
                return CreateDetails(wish);

            attemptedUpdate = (
                wish,
                originalWish);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return CreateDetails(wish);
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolveConcurrentUpdateAsync(
                ownerId,
                wishlistId,
                wishId,
                cancellationToken);
        }
        catch (Exception exception) when (IsWishQuantityBelowReserved(exception))
        {
            throw new WishQuantityBelowReservedException();
        }
        catch (Exception exception)
        {

            if (exception is DependencyUnavailableException ||
                !PostgreSqlFailureClassifier.IsUnavailable(exception))
                throw;

            if (attemptedUpdate is null)
            {
                throw new DependencyUnavailableException(
                    "PostgreSQL",
                    exception);
            }

            return await ResolveAmbiguousUpdateAsync(
                ownerId,
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
        Guid wishId,
        uint expectedVersion,
        CancellationToken cancellationToken)
    {
        var saveAttempted = false;

        try
        {
            var wish = await wishRepository.GetByIdForUpdateAsync(
                wishlistId,
                wishId,
                cancellationToken);

            if (wish is null)
            {
                var access = await GetAccessSafelyAsync(
                    ownerId,
                    wishlistId,
                    cancellationToken);

                if (access is WishlistAccess.MemberNotFound)
                    throw new InvalidAuthenticationSessionException();

                if (access is WishlistAccess.NotOwned)
                    throw new WishlistNotFoundException();

                return false;
            }

            if (wish.Version != expectedVersion)
                throw new WishVersionConflictException();

            wishRepository.Remove(wish);
            saveAttempted = true;
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return await ResolveConcurrentDeletionAsync(
                ownerId,
                wishlistId,
                wishId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            if (exception is DependencyUnavailableException ||
                !PostgreSqlFailureClassifier.IsUnavailable(exception))
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
                wishId,
                exception,
                cancellationToken);
        }
    }

    /// <summary>
    /// Resolves an optimistic concurrency failure while deleting a gift wish.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="false" /> when the wish disappeared from an owned parent.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member disappeared.</exception>
    /// <exception cref="WishlistNotFoundException">The parent wishlist is unavailable to the owner.</exception>
    /// <exception cref="WishVersionConflictException">The wish still exists with another version.</exception>
    private async Task<bool> ResolveConcurrentDeletionAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        var access = await GetAccessSafelyAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        if (access is WishlistAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is WishlistAccess.NotOwned)
            throw new WishlistNotFoundException();

        var currentWish = await GetByIdSafelyAsync(
            wishlistId,
            wishId,
            cancellationToken);

        if (currentWish is null)
            return false;

        throw new WishVersionConflictException();
    }

    /// <summary>
    /// Resolves whether a gift wish deletion committed after its acknowledgement was lost.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    /// <param name="originalException">The transient save exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the wish is no longer available.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member disappeared.</exception>
    /// <exception cref="DependencyUnavailableException">The attempted deletion cannot be confirmed.</exception>
    private async Task<bool> ResolveAmbiguousDeletionAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
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

        var currentWish = await GetByIdSafelyAsync(
            wishlistId,
            wishId,
            cancellationToken);

        if (currentWish is null)
            return true;

        throw new DependencyUnavailableException(
            "PostgreSQL",
            originalException);
    }

    /// <summary>
    /// Resolves a missing wish without revealing an inaccessible parent wishlist.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="null" /> when the owned parent does not contain the wish.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member disappeared.</exception>
    /// <exception cref="WishlistNotFoundException">The parent wishlist is unavailable to the owner.</exception>
    private async Task<WishDetails?> ResolveMissingWishAsync(
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

        if (access is WishlistAccess.NotOwned)
            throw new WishlistNotFoundException();

        return null;
    }

    /// <summary>
    /// Resolves an optimistic concurrency failure against the current private resource state.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="wishlistId">The parent wishlist identifier.</param>
    /// <param name="wishId">The wish identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="null" /> when the wish disappeared from an owned parent.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member disappeared.</exception>
    /// <exception cref="WishlistNotFoundException">The parent wishlist is unavailable to the owner.</exception>
    /// <exception cref="WishVersionConflictException">The wish still exists with a different version.</exception>
    private async Task<WishDetails?> ResolveConcurrentUpdateAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        var access = await GetAccessSafelyAsync(
            ownerId,
            wishlistId,
            cancellationToken);

        if (access is WishlistAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is WishlistAccess.NotOwned)
            throw new WishlistNotFoundException();

        var currentWish = await GetByIdSafelyAsync(
            wishlistId,
            wishId,
            cancellationToken);

        if (currentWish is null)
            return null;

        throw new WishVersionConflictException();
    }

    /// <summary>
    /// Resolves whether a gift wish update committed after its acknowledgement was lost.
    /// </summary>
    /// <param name="ownerId">The authenticated owner identifier.</param>
    /// <param name="attemptedWish">The exact wish state whose save was attempted.</param>
    /// <param name="originalWish">The exact wish state before the attempted update.</param>
    /// <param name="originalException">The transient save exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The committed wish, or <see langword="null" /> when the wish disappeared.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The authenticated member disappeared.</exception>
    /// <exception cref="WishlistNotFoundException">The parent wishlist is unavailable to the owner.</exception>
    /// <exception cref="WishVersionConflictException">A different wish state was committed.</exception>
    /// <exception cref="DependencyUnavailableException">The attempted update cannot be verified.</exception>
    private async Task<WishDetails?> ResolveAmbiguousUpdateAsync(
        Guid ownerId,
        Wish attemptedWish,
        Wish originalWish,
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

        if (currentWish is not null &&
            HasSameClientValues(
                currentWish,
                originalWish))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                originalException);
        }

        var access = await GetAccessSafelyAsync(
            ownerId,
            attemptedWish.WishlistId,
            cancellationToken);

        if (access is WishlistAccess.MemberNotFound)
            throw new InvalidAuthenticationSessionException();

        if (access is WishlistAccess.NotOwned)
            throw new WishlistNotFoundException();

        if (currentWish is null)
            return null;

        throw new WishVersionConflictException();
    }

    /// <summary>
    /// Copies the identifying and editable state needed to reconcile an ambiguous update.
    /// </summary>
    /// <param name="wish">The wish to copy.</param>
    /// <returns>A detached copy of the client-controlled state.</returns>
    private static Wish CopyClientState(Wish wish)
    {
        return new Wish(
            wish.Id,
            wish.WishlistId,
            wish.Name,
            wish.Note,
            wish.Url,
            wish.Price,
            wish.Position,
            wish.Quantity);
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
            wish.Version,
            wish.Quantity);
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
    /// Determines whether a database update exceeded the wishlist gift limit.
    /// </summary>
    /// <param name="exception">The database update exception.</param>
    /// <returns><see langword="true" /> for the expected limit constraint.</returns>
    private static bool IsWishLimitReached(Exception exception)
    {
        var postgresException = exception switch
        {
            PostgresException directException => directException,
            DbUpdateException { InnerException: PostgresException innerException } => innerException,
            _ => null
        };

        return postgresException is
        {
            SqlState: PostgresErrorCodes.CheckViolation,
            ConstraintName: WishCountConstraintName
        };
    }

    /// <summary>
    /// Determines whether a database update reduced a wish below its reserved quantity.
    /// </summary>
    /// <param name="exception">The database update exception.</param>
    /// <returns><see langword="true" /> for the expected reservation invariant violation.</returns>
    private static bool IsWishQuantityBelowReserved(Exception exception)
    {
        var postgresException = exception switch
        {
            PostgresException directException => directException,
            DbUpdateException { InnerException: PostgresException innerException } => innerException,
            _ => null
        };

        return postgresException is
        {
            SqlState: PostgresErrorCodes.CheckViolation,
            ConstraintName: WishQuantityConstraintName
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
