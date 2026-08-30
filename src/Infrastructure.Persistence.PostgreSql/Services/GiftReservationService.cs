using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Manages atomic gift reservations persisted in PostgreSQL.
/// </summary>
public class GiftReservationService : IGiftReservationService
{
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
                "PostgreSQL",
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
                "PostgreSQL",
                exception);
        }
    }

    /// <inheritdoc />
    public async Task<GiftReservationMutationResult> UpsertAsync(
        GiftReservationMutationRequest request,
        CancellationToken cancellationToken)
    {
        GiftReservationMutationResult result;

        try
        {
            await using var transaction = await _transactionFactory.BeginAsync(cancellationToken);
            await ValidateShareLinkAsync(
                request,
                cancellationToken);
            var participant = await ResolveParticipantAsync(
                request,
                cancellationToken);
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
                request.WishId,
                cancellationToken);
            var currentQuantity = currentReservation?.Quantity ?? 0;
            var requestedTotal = totalQuantity - currentQuantity + request.Quantity;

            if (requestedTotal > wish.Quantity && request.Quantity > currentQuantity)
                throw new GiftReservationQuantityUnavailableException();

            var isCreated = currentReservation is null;
            var reservation = currentReservation ?? new GiftReservation(
                    request.ReservationId,
                    request.WishlistId,
                    request.WishId,
                    participant.Id,
                    request.Quantity);

            if (isCreated)
                _giftReservationRepository.Add(reservation);

            var hasChanged = isCreated || reservation.UpdateQuantity(request.Quantity);

            if (hasChanged)
                await _unitOfWork.SaveChangesAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            result = CreateResult(
                reservation,
                isCreated);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new GiftReservationVersionConflictException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
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
            await ValidateShareLinkAsync(
                request.ShareLinkId,
                request.WishlistId,
                request.ShareSecret,
                cancellationToken);
            var participant = await ResolveParticipantAsync(
                request.MemberId,
                request.GuestToken,
                request.WishlistId,
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
                    "PostgreSQL",
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

    private async Task ValidateShareLinkAsync(
        GiftReservationMutationRequest request,
        CancellationToken cancellationToken)
    {
        await ValidateShareLinkAsync(
            request.ShareLinkId,
            request.WishlistId,
            request.ShareSecret,
            cancellationToken);
    }

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

    private async Task<WishlistParticipant> ResolveParticipantAsync(
        GiftReservationMutationRequest request,
        CancellationToken cancellationToken)
    {
        return await ResolveParticipantAsync(
            request.MemberId,
            request.GuestToken,
            request.WishlistId,
            cancellationToken);
    }

    private async Task<WishlistParticipant> ResolveParticipantAsync(
        Guid? memberIdValue,
        string? guestToken,
        Guid wishlistId,
        CancellationToken cancellationToken)
    {
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
            "PostgreSQL",
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
                "PostgreSQL",
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
