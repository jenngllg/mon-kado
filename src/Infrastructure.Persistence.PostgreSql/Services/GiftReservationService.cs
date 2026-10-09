using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Manages atomic gift reservations persisted in PostgreSQL.
/// </summary>
public class GiftReservationService : IGiftReservationService
{
    private const string PostgreSqlDependencyName = "PostgreSQL";

    private readonly IGiftReservationRepository _giftReservationRepository;
    private readonly IGuestSessionRepository _guestSessionRepository;
    private readonly IGuestSessionTokenService _guestSessionTokenService;
    private readonly IWishlistParticipantRepository _participantRepository;
    private readonly IGiftReservationTransactionFactory _transactionFactory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly IWishlistShareTokenService _wishlistShareTokenService;

    /// <summary>
    /// Initializes a gift reservation service.
    /// </summary>
    /// <param name="giftReservationRepository">The reservation repository.</param>
    /// <param name="participantRepository">The participant repository.</param>
    /// <param name="guestSessionRepository">The guest-session repository.</param>
    /// <param name="guestSessionTokenService">The guest-session token service.</param>
    /// <param name="wishlistShareTokenService">The share-link token service.</param>
    /// <param name="transactionFactory">The transaction factory.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="timeProvider">The time provider.</param>
    [SuppressMessage(
        "CodeQuality",
        "S107:Methods should not have too many parameters",
        Justification = "The constructor keeps independently testable persistence and security dependencies explicit.")]
    public GiftReservationService(
        IGiftReservationRepository giftReservationRepository,
        IWishlistParticipantRepository participantRepository,
        IGuestSessionRepository guestSessionRepository,
        IGuestSessionTokenService guestSessionTokenService,
        IWishlistShareTokenService wishlistShareTokenService,
        IGiftReservationTransactionFactory transactionFactory,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _giftReservationRepository = giftReservationRepository;
        _participantRepository = participantRepository;
        _guestSessionRepository = guestSessionRepository;
        _guestSessionTokenService = guestSessionTokenService;
        _wishlistShareTokenService = wishlistShareTokenService;
        _transactionFactory = transactionFactory;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<GiftReservationDetails?> GetOwnedAsync(
        Guid ownerId,
        Guid wishlistId,
        Guid wishId,
        CancellationToken cancellationToken)
    {
        GiftReservationDetails? reservation = null;

        try
        {
            await using var transaction = await _transactionFactory.BeginAsync(cancellationToken);
            await _transactionFactory.LockOwnedWishlistAsync(
                ownerId,
                wishlistId,
                cancellationToken);
            _ = await _transactionFactory.LockWishAsync(
                wishlistId,
                wishId,
                cancellationToken) ?? throw new WishNotFoundException();
            var participant = await _participantRepository.GetByMemberForUpdateAsync(
                wishlistId,
                ownerId,
                cancellationToken);

            if (participant is not null)
            {
                reservation = await GetAsync(
                    wishlistId,
                    wishId,
                    participant.Id,
                    cancellationToken);
            }
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                PostgreSqlDependencyName,
                exception);
        }

        return reservation;
    }

    /// <inheritdoc />
    public async Task<GiftReservationDetails?> GetAsync(
        Guid wishlistId,
        Guid wishId,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        try
        {
            var reservation = await _giftReservationRepository.GetAsync(
                wishlistId,
                wishId,
                participantId,
                cancellationToken);

            return reservation is null
                ? null
                : CreateDetails(reservation);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                PostgreSqlDependencyName,
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, int>> GetQuantitiesAsync(
        Guid wishlistId,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _giftReservationRepository.GetQuantitiesAsync(
                wishlistId,
                participantId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                PostgreSqlDependencyName,
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<GiftReservationMutationResult> UpsertAsync(
        GiftReservationMutationRequest request,
        CancellationToken cancellationToken)
    {
        GiftReservationMutationResult result;
        var participantId = Guid.Empty;
        var reservationId = Guid.Empty;
        var attemptedQuantity = 0;
        var isCreated = false;
        var commitAttempted = false;

        try
        {
            await using var transaction = await _transactionFactory.BeginAsync(cancellationToken);

            if (request.MemberId is Guid authenticatedMemberId)
            {
                await _transactionFactory.LockMemberAsync(
                    authenticatedMemberId,
                    cancellationToken);
            }

            await ValidateAccessAsync(
                request,
                cancellationToken);
            var participant = await ResolveParticipantAsync(
                request,
                cancellationToken);
            participantId = participant.Id;
            var wish = await _transactionFactory.LockWishAsync(
                request.WishlistId,
                request.WishId,
                cancellationToken) ?? throw new WishNotFoundException();
            var currentReservation = await _giftReservationRepository.GetForUpdateAsync(
                request.WishlistId,
                request.WishId,
                participant.Id,
                cancellationToken);
            ValidateVersion(
                currentReservation,
                request.ExpectedVersion);
            var totalQuantity = await _giftReservationRepository.GetTotalQuantityAsync(
                request.WishlistId,
                request.WishId,
                cancellationToken);
            var currentQuantity = currentReservation?.Quantity ?? 0;
            var requestedTotal = totalQuantity - currentQuantity + request.Quantity;

            if (requestedTotal > wish.Quantity && request.Quantity > currentQuantity)
                throw new GiftReservationQuantityUnavailableException();

            isCreated = currentReservation is null;
            var reservation = currentReservation ?? new GiftReservation(
                    request.ReservationId,
                    request.WishlistId,
                    request.WishId,
                    participant.Id,
                    request.Quantity);

            if (isCreated)
                _giftReservationRepository.Add(reservation);

            var hasChanged = isCreated || reservation.UpdateQuantity(request.Quantity);

            if (hasChanged && participant.MemberId is Guid memberId)
            {
                await TrackUpsertAsync(
                    reservation,
                    memberId,
                    isCreated,
                    _timeProvider.GetUtcNow().UtcDateTime,
                    cancellationToken);
            }

            if (hasChanged)
                await _unitOfWork.SaveChangesAsync(cancellationToken);

            reservationId = reservation.Id;
            attemptedQuantity = reservation.Quantity;
            commitAttempted = true;
            await transaction.CommitAsync(cancellationToken);
            result = CreateResult(
                reservation,
                isCreated);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new GiftReservationVersionConflictException();
        }
        catch (Exception exception)
        {
            if (exception is DependencyUnavailableException ||
                !PostgreSqlFailureClassifier.IsUnavailable(exception))
            {
                throw;
            }

            if (!commitAttempted)
            {
                throw new DependencyUnavailableException(
                    PostgreSqlDependencyName,
                    exception);
            }

            result = await ResolveAmbiguousUpsertAsync(
                request,
                participantId,
                reservationId,
                attemptedQuantity,
                isCreated,
                exception,
                cancellationToken);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<bool> CancelAsync(
        GiftReservationCancellationRequest request,
        CancellationToken cancellationToken)
    {
        bool result;
        var participantId = Guid.Empty;
        var reservationId = Guid.Empty;
        var commitAttempted = false;

        try
        {
            await using var transaction = await _transactionFactory.BeginAsync(cancellationToken);

            if (request.MemberId is Guid authenticatedMemberId)
            {
                await _transactionFactory.LockMemberAsync(
                    authenticatedMemberId,
                    cancellationToken);
            }

            await ValidateAccessAsync(
                request,
                cancellationToken);
            var participant = await ResolveParticipantAsync(
                request.MemberId,
                request.GuestToken,
                request.WishlistId,
                request.IsOwnerReservation,
                false,
                cancellationToken);
            participantId = participant.Id;
            _ = await _transactionFactory.LockWishAsync(
                request.WishlistId,
                request.WishId,
                cancellationToken) ?? throw new WishNotFoundException();
            var reservation = await _giftReservationRepository.GetForUpdateAsync(
                request.WishlistId,
                request.WishId,
                participantId,
                cancellationToken);

            if (reservation is null)
                return false;

            if (reservation.Version != request.ExpectedVersion)
                throw new GiftReservationVersionConflictException();

            if (participant.MemberId is Guid memberId)
            {
                await TrackCancellationAsync(
                    reservation,
                    memberId,
                    _timeProvider.GetUtcNow().UtcDateTime,
                    cancellationToken);
            }

            reservationId = reservation.Id;
            _giftReservationRepository.Remove(reservation);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            commitAttempted = true;
            await transaction.CommitAsync(cancellationToken);
            result = true;
        }
        catch (DbUpdateConcurrencyException)
        {
            result = await ResolveConcurrentCancellationAsync(
                request,
                participantId,
                cancellationToken);
        }
        catch (Exception exception)
        {
            if (exception is DependencyUnavailableException ||
                !PostgreSqlFailureClassifier.IsUnavailable(exception))
            {
                throw;
            }

            if (!commitAttempted)
            {
                throw new DependencyUnavailableException(
                    PostgreSqlDependencyName,
                    exception);
            }

            result = await ResolveAmbiguousCancellationAsync(
                request,
                participantId,
                reservationId,
                exception,
                cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Creates or updates the durable history for a member reservation.
    /// </summary>
    /// <param name="reservation">The current reservation.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="isCreated">Whether the reservation has just been created.</param>
    /// <param name="activityAt">The UTC activity date and time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="WishNotFoundException">The reservation source no longer exists.</exception>
    private async Task TrackUpsertAsync(
        GiftReservation reservation,
        Guid memberId,
        bool isCreated,
        DateTime activityAt,
        CancellationToken cancellationToken)
    {
        var history = isCreated
            ? null
            : await _giftReservationRepository.GetHistoryForUpdateAsync(
                reservation.Id,
                cancellationToken);

        if (history is not null)
        {
            history.UpdateQuantity(
                reservation.Quantity,
                activityAt);

            return;
        }

        var source = await _giftReservationRepository.GetHistorySourceAsync(
            reservation.WishlistId,
            reservation.WishId,
            cancellationToken) ?? throw new WishNotFoundException();
        var createdAt = isCreated
            ? activityAt
            : reservation.CreatedAt;
        var newHistory = new GiftReservationHistory(
            reservation.Id,
            memberId,
            reservation.WishlistId,
            source.WishlistName,
            reservation.WishId,
            source.WishName,
            reservation.Quantity,
            createdAt,
            activityAt);
        _giftReservationRepository.AddHistory(newHistory);
    }

    /// <summary>
    /// Ends the durable history for a cancelled member reservation.
    /// </summary>
    /// <param name="reservation">The reservation being cancelled.</param>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="cancelledAt">The UTC cancellation date and time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <exception cref="WishNotFoundException">The reservation source no longer exists.</exception>
    private async Task TrackCancellationAsync(
        GiftReservation reservation,
        Guid memberId,
        DateTime cancelledAt,
        CancellationToken cancellationToken)
    {
        var history = await _giftReservationRepository.GetHistoryForUpdateAsync(
            reservation.Id,
            cancellationToken);

        if (history is null)
        {
            var source = await _giftReservationRepository.GetHistorySourceAsync(
                reservation.WishlistId,
                reservation.WishId,
                cancellationToken) ?? throw new WishNotFoundException();
            history = new GiftReservationHistory(
                reservation.Id,
                memberId,
                reservation.WishlistId,
                source.WishlistName,
                reservation.WishId,
                source.WishName,
                reservation.Quantity,
                reservation.CreatedAt,
                reservation.CreatedAt);
            _giftReservationRepository.AddHistory(history);
        }

        history.End(
            GiftReservationHistoryStatus.Cancelled,
            cancelledAt);
    }

    /// <summary>Validates the server-selected access model for a reservation mutation.</summary>
    /// <param name="request">The server-created mutation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed when the source parent is authorized and locked.</returns>
    private async Task ValidateAccessAsync(
        GiftReservationMutationRequest request,
        CancellationToken cancellationToken)
    {

        if (await ValidateOwnedAccessAsync(
            request.IsOwnerReservation,
            request.MemberId,
            request.WishlistId,
            cancellationToken))
            return;

        await ValidateShareLinkAsync(
            request.ShareLinkId,
            request.WishlistId,
            request.ShareSecret,
            cancellationToken);
    }

    /// <summary>Validates the server-selected access model for a cancellation.</summary>
    /// <param name="request">The server-created cancellation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed when the parent is authorized and locked.</returns>
    private async Task ValidateAccessAsync(
        GiftReservationCancellationRequest request,
        CancellationToken cancellationToken)
    {

        if (await ValidateOwnedAccessAsync(
            request.IsOwnerReservation,
            request.MemberId,
            request.WishlistId,
            cancellationToken))
            return;

        await ValidateShareLinkAsync(
            request.ShareLinkId,
            request.WishlistId,
            request.ShareSecret,
            cancellationToken);
    }

    /// <summary>Fences private owner operations without weakening shared-link validation.</summary>
    /// <param name="isOwnerReservation">The trusted private-route discriminator.</param>
    /// <param name="memberId">The authenticated member.</param>
    /// <param name="wishlistId">The parent.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether private ownership has been validated.</returns>
    private async Task<bool> ValidateOwnedAccessAsync(
        bool isOwnerReservation,
        Guid? memberId,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {

        if (!isOwnerReservation)
            return false;

        await _transactionFactory.LockOwnedWishlistAsync(
            memberId.GetValueOrDefault(),
            wishlistId,
            cancellationToken);

        return true;
    }

    /// <summary>Revalidates shared access while holding its parent lock.</summary>
    /// <param name="shareLinkId">The shared link.</param>
    /// <param name="wishlistId">The parent.</param>
    /// <param name="shareSecret">The presented secret.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed when access is valid.</returns>
    private async Task ValidateShareLinkAsync(
        Guid shareLinkId,
        Guid wishlistId,
        string shareSecret,
        CancellationToken cancellationToken)
    {
        var shareLink = await _transactionFactory.LockShareLinkAsync(
            shareLinkId,
            cancellationToken);

        if (shareLink is null ||
            shareLink.WishlistId != wishlistId ||
            !_wishlistShareTokenService.Verify(
                shareSecret,
                shareLink.SecretHash))
        {
            throw new SharedWishlistNotFoundException();
        }
    }

    /// <summary>Resolves the server-selected participant for an explicit mutation.</summary>
    /// <param name="request">The trusted mutation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The authorized participation.</returns>
    private async Task<WishlistParticipant> ResolveParticipantAsync(
        GiftReservationMutationRequest request,
        CancellationToken cancellationToken)
    {

        return await ResolveParticipantAsync(
            request.MemberId,
            request.GuestToken,
            request.WishlistId,
            request.IsOwnerReservation,
            true,
            cancellationToken);
    }

    /// <summary>Resolves shared participation or the privately authorized owner.</summary>
    /// <param name="memberIdValue">The authenticated member.</param>
    /// <param name="guestToken">The guest credential.</param>
    /// <param name="wishlistId">The parent.</param>
    /// <param name="isOwnerReservation">The server-selected private access mode.</param>
    /// <param name="createOwnerParticipant">Whether this explicit write may create owner participation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The authorized participant.</returns>
    private async Task<WishlistParticipant> ResolveParticipantAsync(
        Guid? memberIdValue,
        string? guestToken,
        Guid wishlistId,
        bool isOwnerReservation,
        bool createOwnerParticipant,
        CancellationToken cancellationToken)
    {

        if (isOwnerReservation)
        {
            return await ResolveOwnerParticipantAsync(
                memberIdValue.GetValueOrDefault(),
                wishlistId,
                createOwnerParticipant,
                cancellationToken);
        }

        if (memberIdValue is Guid memberId)
        {
            var displayName = await _participantRepository.GetMemberDisplayNameAsync(
                memberId,
                cancellationToken);

            if (displayName is null)
                throw new InvalidAuthenticationSessionException();

            return await _participantRepository.GetByMemberForUpdateAsync(
                wishlistId,
                memberId,
                cancellationToken) ?? throw new WishlistParticipantNotFoundException();
        }

        if (guestToken is null ||
            !_guestSessionTokenService.TryParse(
                guestToken,
                out var guestSessionId,
                out var presentedHash))
        {
            throw new GuestSessionInvalidException();
        }

        var guestSession = await _guestSessionRepository.GetByIdAsync(
            guestSessionId,
            cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        if (guestSession is null ||
            guestSession.ExpiresAt <= now ||
            !_guestSessionTokenService.Verify(
                presentedHash,
                guestSession.SecretHash))
        {
            throw new GuestSessionInvalidException();
        }

        return await _participantRepository.GetByGuestSessionForUpdateAsync(
            wishlistId,
            guestSessionId,
            cancellationToken) ?? throw new WishlistParticipantNotFoundException();
    }

    /// <summary>Uses one member participation per parent, creating it only inside an explicit reservation write.</summary>
    /// <param name="ownerId">The authorized owner.</param>
    /// <param name="wishlistId">The locked parent.</param>
    /// <param name="createIfMissing">Whether this operation may create participation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The owner's participant.</returns>
    private async Task<WishlistParticipant> ResolveOwnerParticipantAsync(
        Guid ownerId,
        Guid wishlistId,
        bool createIfMissing,
        CancellationToken cancellationToken)
    {
        var participant = await _participantRepository.GetByMemberForUpdateAsync(
            wishlistId,
            ownerId,
            cancellationToken);

        if (participant is not null)
            return participant;

        if (!createIfMissing)
            throw new GiftReservationNotFoundException();

        var created = WishlistParticipant.CreateMember(
            Guid.CreateVersion7(),
            wishlistId,
            ownerId);
        _participantRepository.Add(created);

        return created;
    }

    private async Task<bool> ResolveConcurrentCancellationAsync(
        GiftReservationCancellationRequest request,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        var currentReservation = await GetReservationSafelyAsync(
            request.WishlistId,
            request.WishId,
            participantId,
            cancellationToken);

        if (currentReservation is null)
            return false;

        throw new GiftReservationVersionConflictException();
    }

    /// <summary>
    /// Resolves whether a reservation mutation committed after its acknowledgement was lost.
    /// </summary>
    /// <param name="request">The attempted mutation.</param>
    /// <param name="participantId">The resolved participant identifier.</param>
    /// <param name="reservationId">The attempted reservation identifier.</param>
    /// <param name="attemptedQuantity">The attempted quantity.</param>
    /// <param name="isCreated">Whether the attempted mutation created the reservation.</param>
    /// <param name="originalException">The transient commit exception.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The committed mutation result.</returns>
    /// <exception cref="DependencyUnavailableException">The attempted mutation cannot be verified.</exception>
    private async Task<GiftReservationMutationResult> ResolveAmbiguousUpsertAsync(
        GiftReservationMutationRequest request,
        Guid participantId,
        Guid reservationId,
        int attemptedQuantity,
        bool isCreated,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        var currentReservation = await GetReservationSafelyAsync(
            request.WishlistId,
            request.WishId,
            participantId,
            cancellationToken);

        if (currentReservation is not null &&
            currentReservation.Id == reservationId &&
            currentReservation.Quantity == attemptedQuantity)
        {
            return CreateResult(
                currentReservation,
                isCreated);
        }

        throw new DependencyUnavailableException(
            PostgreSqlDependencyName,
            originalException);
    }

    private async Task<bool> ResolveAmbiguousCancellationAsync(
        GiftReservationCancellationRequest request,
        Guid participantId,
        Guid reservationId,
        Exception originalException,
        CancellationToken cancellationToken)
    {
        var currentReservation = await GetReservationSafelyAsync(
            request.WishlistId,
            request.WishId,
            participantId,
            cancellationToken);

        if (currentReservation is null)
            return true;

        if (currentReservation.Id != reservationId)
            throw new GiftReservationVersionConflictException();

        throw new DependencyUnavailableException(
            PostgreSqlDependencyName,
            originalException);
    }

    private async Task<GiftReservation?> GetReservationSafelyAsync(
        Guid wishlistId,
        Guid wishId,
        Guid participantId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _giftReservationRepository.GetAsync(
                wishlistId,
                wishId,
                participantId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                PostgreSqlDependencyName,
                exception);
        }
    }

    private static void ValidateVersion(
        GiftReservation? reservation,
        uint? expectedVersion)
    {
        if (reservation is null && expectedVersion is not null)
            throw new GiftReservationVersionConflictException();

        if (reservation is not null && expectedVersion is null)
            throw new PreconditionRequiredException();

        if (reservation is not null && reservation.Version != expectedVersion)
            throw new GiftReservationVersionConflictException();
    }

    private static GiftReservationMutationResult CreateResult(
        GiftReservation reservation,
        bool isCreated)
    {
        return new GiftReservationMutationResult
        {
            Reservation = CreateDetails(reservation),
            IsCreated = isCreated
        };
    }

    private static GiftReservationDetails CreateDetails(GiftReservation reservation)
    {
        return new GiftReservationDetails
        {
            Id = reservation.Id,
            WishId = reservation.WishId,
            Quantity = reservation.Quantity,
            CreatedAt = reservation.CreatedAt,
            UpdatedAt = reservation.UpdatedAt,
            Version = reservation.Version
        };
    }
}
