using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

using Microsoft.Extensions.Options;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Manages member and browser guest participation persisted in PostgreSQL.
/// </summary>
public class WishlistParticipantService : IWishlistParticipantService
{
    /// <summary>Gets the maximum number of active participants per wishlist.</summary>
    public const int MaximumParticipantCount = 100;

    private readonly IGuestSessionRepository _guestSessionRepository;
    private readonly IGuestSessionTokenService _guestSessionTokenService;
    private readonly IGiftReservationRepository _giftReservationRepository;
    private readonly GuestSessionOptions _options;
    private readonly IWishlistParticipantRepository _participantRepository;
    private readonly IWishlistParticipantTransactionFactory _transactionFactory;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;
    private readonly IWishlistShareTokenService _wishlistShareTokenService;

    /// <summary>Initializes the wishlist participant service.</summary>
    /// <param name="participantRepository">The participant repository.</param>
    /// <param name="guestSessionRepository">The guest-session repository.</param>
    /// <param name="guestSessionTokenService">The guest-session token service.</param>
    /// <param name="giftReservationRepository">The gift reservation repository.</param>
    /// <param name="wishlistShareTokenService">The share-link token service.</param>
    /// <param name="transactionFactory">The participant transaction factory.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="options">The guest-session options.</param>
    [SuppressMessage(
        "CodeQuality",
        "S107:Methods should not have too many parameters",
        Justification = "The constructor keeps independently testable persistence and security dependencies explicit.")]
    public WishlistParticipantService(
        IWishlistParticipantRepository participantRepository,
        IGuestSessionRepository guestSessionRepository,
        IGuestSessionTokenService guestSessionTokenService,
        IGiftReservationRepository giftReservationRepository,
        IWishlistShareTokenService wishlistShareTokenService,
        IWishlistParticipantTransactionFactory transactionFactory,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider,
        IOptions<GuestSessionOptions> options)
    {
        _participantRepository = participantRepository;
        _guestSessionRepository = guestSessionRepository;
        _guestSessionTokenService = guestSessionTokenService;
        _giftReservationRepository = giftReservationRepository;
        _wishlistShareTokenService = wishlistShareTokenService;
        _transactionFactory = transactionFactory;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<WishlistParticipantJoinResult> JoinAsync(
        WishlistParticipantJoinRequest request,
        CancellationToken cancellationToken)
    {
        WishlistParticipantJoinResult result;

        try
        {
            await using var transaction = await _transactionFactory.BeginAsync(cancellationToken);
            var shareLink = await _transactionFactory.LockShareLinkAsync(
                request.ShareLinkId,
                cancellationToken);

            if (shareLink is null ||
                shareLink.WishlistId != request.WishlistId ||
                !_wishlistShareTokenService.Verify(
                    request.ShareSecret,
                    shareLink.SecretHash))
            {
                throw new SharedWishlistNotFoundException();
            }

            var ownerId = await _transactionFactory.LockWishlistAsync(
                request.WishlistId,
                cancellationToken);

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var validGuestSessionId = await ResolveGuestSessionIdAsync(
                request.GuestToken,
                now,
                cancellationToken);
            result = request.MemberId is null
                ? await JoinGuestAsync(
                    request.ParticipantId,
                    request.GuestSessionId,
                    request.WishlistId,
                    validGuestSessionId,
                    request.DisplayName,
                    now,
                    cancellationToken)
                : await JoinMemberAsync(
                    request.ParticipantId,
                    request.WishlistId,
                    request.MemberId.Value,
                    ownerId,
                    validGuestSessionId,
                    cancellationToken);
            await transaction.CommitAsync(cancellationToken);
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
    public async Task<WishlistParticipantLookupResult> GetCurrentAsync(
        Guid wishlistId,
        Guid? memberId,
        string? guestToken,
        CancellationToken cancellationToken)
    {
        try
        {
            if (memberId is not null)
                return await GetMemberParticipantAsync(
                    wishlistId,
                    memberId.Value,
                    cancellationToken);

            if (guestToken is null)
            {
                return new WishlistParticipantLookupResult(
                    WishlistParticipantLookupOutcome.MissingIdentity,
                    null);
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var guestSessionId = await ResolveGuestSessionIdAsync(
                guestToken,
                now,
                cancellationToken);

            if (guestSessionId is null)
            {
                return new WishlistParticipantLookupResult(
                    WishlistParticipantLookupOutcome.InvalidGuestSession,
                    null);
            }

            var participant = await _participantRepository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                guestSessionId.Value,
                cancellationToken);

            if (participant is null)
            {
                return new WishlistParticipantLookupResult(
                    WishlistParticipantLookupOutcome.NotJoined,
                    null);
            }

            return new WishlistParticipantLookupResult(
                WishlistParticipantLookupOutcome.Found,
                new WishlistParticipantDetails(
                    participant.Id,
                    participant.GuestDisplayName));
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {
            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }

    private async Task<WishlistParticipantJoinResult> JoinGuestAsync(
        Guid participantId,
        Guid guestSessionId,
        Guid wishlistId,
        Guid? validGuestSessionId,
        string? displayName,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (validGuestSessionId is not null)
        {
            var currentParticipant = await _participantRepository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                validGuestSessionId.Value,
                cancellationToken);

            if (currentParticipant is not null)
            {
                return new WishlistParticipantJoinResult(
                    new WishlistParticipantDetails(
                        currentParticipant.Id,
                        currentParticipant.GuestDisplayName),
                    false,
                    null,
                    null);
            }

            await EnsureCapacityAsync(
                wishlistId,
                now,
                cancellationToken);
            var participant = new WishlistParticipant(
                participantId,
                wishlistId,
                validGuestSessionId.Value,
                NormalizeDisplayName(displayName));
            _participantRepository.Add(participant);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new WishlistParticipantJoinResult(
                new WishlistParticipantDetails(
                    participant.Id,
                    participant.GuestDisplayName),
                true,
                null,
                null);
        }

        await EnsureCapacityAsync(
            wishlistId,
            now,
            cancellationToken);
        var token = _guestSessionTokenService.Create(guestSessionId);
        var expiresAt = now.Add(_options.Lifetime);
        var session = new GuestSession(
            guestSessionId,
            token.SecretHash,
            expiresAt);
        var newParticipant = new WishlistParticipant(
            participantId,
            wishlistId,
            guestSessionId,
            NormalizeDisplayName(displayName));
        _guestSessionRepository.Add(session);
        _participantRepository.Add(newParticipant);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new WishlistParticipantJoinResult(
            new WishlistParticipantDetails(
                newParticipant.Id,
                newParticipant.GuestDisplayName),
            true,
            token.Secret,
            expiresAt);
    }

    private async Task<WishlistParticipantJoinResult> JoinMemberAsync(
        Guid participantId,
        Guid wishlistId,
        Guid memberId,
        Guid ownerId,
        Guid? validGuestSessionId,
        CancellationToken cancellationToken)
    {
        var displayName = await _participantRepository.GetMemberDisplayNameAsync(
            memberId,
            cancellationToken) ?? throw new InvalidAuthenticationSessionException();

        if (memberId == ownerId)
            throw new WishlistOwnerCannotJoinException();

        var memberParticipant = await _participantRepository.GetByMemberForUpdateAsync(
            wishlistId,
            memberId,
            cancellationToken);
        var guestParticipant = validGuestSessionId is null
            ? null
            : await _participantRepository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                validGuestSessionId.Value,
                cancellationToken);

        if (memberParticipant is not null)
        {
            if (guestParticipant is not null && guestParticipant.Id != memberParticipant.Id)
            {
                await MergeReservationsAsync(
                    guestParticipant.Id,
                    memberParticipant.Id,
                    cancellationToken);
                _participantRepository.Remove(guestParticipant);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return new WishlistParticipantJoinResult(
                new WishlistParticipantDetails(
                    memberParticipant.Id,
                    displayName),
                false,
                null,
                null);
        }

        if (guestParticipant is not null)
        {
            guestParticipant.AttachToMember(memberId);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new WishlistParticipantJoinResult(
                new WishlistParticipantDetails(
                    guestParticipant.Id,
                    displayName),
                false,
                null,
                null);
        }

        await EnsureCapacityAsync(
            wishlistId,
            _timeProvider.GetUtcNow().UtcDateTime,
            cancellationToken);
        var participant = WishlistParticipant.CreateMember(
            participantId,
            wishlistId,
            memberId);
        _participantRepository.Add(participant);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new WishlistParticipantJoinResult(
            new WishlistParticipantDetails(
                participant.Id,
                displayName),
            true,
            null,
            null);
    }

    private async Task<WishlistParticipantLookupResult> GetMemberParticipantAsync(
        Guid wishlistId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        var displayName = await _participantRepository.GetMemberDisplayNameAsync(
            memberId,
            cancellationToken);

        if (displayName is null)
        {
            return new WishlistParticipantLookupResult(
                WishlistParticipantLookupOutcome.MemberNotFound,
                null);
        }

        var participant = await _participantRepository.GetByMemberForUpdateAsync(
            wishlistId,
            memberId,
            cancellationToken);

        return participant is null
            ? new WishlistParticipantLookupResult(
                WishlistParticipantLookupOutcome.NotJoined,
                null)
            : new WishlistParticipantLookupResult(
                WishlistParticipantLookupOutcome.Found,
                new WishlistParticipantDetails(
                    participant.Id,
                    displayName));
    }

    private async Task MergeReservationsAsync(
        Guid guestParticipantId,
        Guid memberParticipantId,
        CancellationToken cancellationToken)
    {
        var guestReservations = await _giftReservationRepository.GetByParticipantForUpdateAsync(
            guestParticipantId,
            cancellationToken);
        var memberReservations = await _giftReservationRepository.GetByParticipantForUpdateAsync(
            memberParticipantId,
            cancellationToken);
        var memberReservationsByWishId = memberReservations.ToDictionary(
            reservation => reservation.WishId);

        foreach (var guestReservation in guestReservations)
        {
            if (memberReservationsByWishId.TryGetValue(
                guestReservation.WishId,
                out var memberReservation))
            {
                memberReservation.UpdateQuantity(
                    memberReservation.Quantity + guestReservation.Quantity);
                _giftReservationRepository.Remove(guestReservation);

                continue;
            }

            guestReservation.TransferTo(memberParticipantId);
        }
    }

    private async Task<Guid?> ResolveGuestSessionIdAsync(
        string? guestToken,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (guestToken is null ||
            !_guestSessionTokenService.TryParse(
                guestToken,
                out var sessionId,
                out var presentedHash))
        {
            return null;
        }

        var session = await _guestSessionRepository.GetByIdAsync(
            sessionId,
            cancellationToken);

        if (session is null ||
            session.ExpiresAt <= now ||
            !_guestSessionTokenService.Verify(
                presentedHash,
                session.SecretHash))
        {
            return null;
        }

        return session.Id;
    }

    private async Task EnsureCapacityAsync(
        Guid wishlistId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var participantCount = await _participantRepository.CountActiveAsync(
            wishlistId,
            now,
            cancellationToken);

        if (participantCount >= MaximumParticipantCount)
            throw new WishlistParticipantLimitReachedException();
    }

    private static string NormalizeDisplayName(string? displayName)
    {
        return displayName?.Trim() ?? string.Empty;
    }
}
