using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.Extensions.Options;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class WishlistParticipantServiceTests
{
    private readonly DateTime _now = new(
        2026,
        8,
        26,
        12,
        0,
        0,
        DateTimeKind.Utc);
    private readonly Mock<IGuestSessionRepository> _guestSessionRepositoryMock;
    private readonly Mock<IGiftReservationRepository> _giftReservationRepositoryMock;
    private readonly Mock<IWishlistParticipantRepository> _participantRepositoryMock;
    private readonly WishlistParticipantService _service;
    private readonly GuestSessionTokenService _tokenService = new();
    private readonly Mock<IWishlistParticipantTransactionFactory> _transactionFactoryMock;
    private readonly Mock<IWishlistParticipantTransaction> _transactionMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IWishlistShareTokenService> _wishlistShareTokenServiceMock;

    public WishlistParticipantServiceTests()
    {
        _participantRepositoryMock = new Mock<IWishlistParticipantRepository>(MockBehavior.Strict);
        _guestSessionRepositoryMock = new Mock<IGuestSessionRepository>(MockBehavior.Strict);
        _giftReservationRepositoryMock = new Mock<IGiftReservationRepository>(MockBehavior.Strict);
        _transactionFactoryMock = new Mock<IWishlistParticipantTransactionFactory>(MockBehavior.Strict);
        _transactionMock = new Mock<IWishlistParticipantTransaction>(MockBehavior.Strict);
        _unitOfWorkMock = new Mock<IUnitOfWork>(MockBehavior.Strict);
        _wishlistShareTokenServiceMock = new Mock<IWishlistShareTokenService>(MockBehavior.Strict);
        _transactionMock
            .Setup(transaction => transaction.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        _service = new WishlistParticipantService(
            _participantRepositoryMock.Object,
            _guestSessionRepositoryMock.Object,
            _tokenService,
            _giftReservationRepositoryMock.Object,
            _wishlistShareTokenServiceMock.Object,
            _transactionFactoryMock.Object,
            _unitOfWorkMock.Object,
            new FixedTimeProvider(_now),
            Microsoft.Extensions.Options.Options.Create(new GuestSessionOptions()));
    }

    [Fact]
    public async Task JoinAsync_WhenAnonymousSessionIsNew_CreatesGuestAndParticipant()
    {
        // Arrange
        var participantId = Guid.CreateVersion7();
        var guestSessionId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        GuestSession? addedSession = null;
        WishlistParticipant? addedParticipant = null;
        SetupTransaction(
            wishlistId,
            ownerId,
            cancellationToken);
        SetupCapacity(
            wishlistId,
            0,
            cancellationToken);
        _guestSessionRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<GuestSession>()))
            .Callback<GuestSession>(session => addedSession = session);
        _participantRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<WishlistParticipant>()))
            .Callback<WishlistParticipant>(participant => addedParticipant = participant);
        SetupSaveAndCommit(cancellationToken);

        // Act
        var result = await _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = participantId,
                GuestSessionId = guestSessionId,
                WishlistId = wishlistId,
                ShareLinkId = wishlistId,
                ShareSecret = "share-secret",
                DisplayName = "  Jenn  "
            },
            cancellationToken);

        // Assert
        Assert.True(result.IsCreated);
        Assert.Equal(
            participantId,
            result.Participant.Id);
        Assert.Equal(
            "Jenn",
            result.Participant.DisplayName);
        Assert.NotNull(result.GuestToken);
        Assert.Equal(
            _now.AddDays(180),
            result.GuestTokenExpiresAt);
        Assert.Equal(
            guestSessionId,
            addedSession?.Id);
        Assert.Equal(
            _now.AddDays(180),
            addedSession?.ExpiresAt);
        Assert.Equal(
            participantId,
            addedParticipant?.Id);
        Assert.Equal(
            guestSessionId,
            addedParticipant?.GuestSessionId);
        VerifyTransaction(
            wishlistId,
            commits: true,
            cancellationToken: cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.CountActiveAsync(
                wishlistId,
                _now,
                cancellationToken),
            Times.Once);
        _participantRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<WishlistParticipant>()),
            Times.Once);
        _guestSessionRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<GuestSession>()),
            Times.Once);
        VerifySave(cancellationToken);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JoinAsync_WhenGuestAlreadyJoined_ReturnsExistingParticipant()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var participant = new WishlistParticipant(
            Guid.CreateVersion7(),
            wishlistId,
            Guid.CreateVersion7(),
            "Jenn");
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = SetupValidGuest(
            participant.GuestSessionId.GetValueOrDefault(),
            cancellationToken);
        SetupTransaction(
            wishlistId,
            ownerId,
            cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                participant.GuestSessionId.GetValueOrDefault(),
                cancellationToken))
            .ReturnsAsync(participant);
        SetupCommit(cancellationToken);

        // Act
        var result = await _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = Guid.CreateVersion7(),
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                ShareLinkId = wishlistId,
                ShareSecret = "share-secret",
                GuestToken = token.Secret,
                DisplayName = "Different"
            },
            cancellationToken);

        // Assert
        Assert.False(result.IsCreated);
        Assert.Equal(
            participant.Id,
            result.Participant.Id);
        Assert.Equal(
            "Jenn",
            result.Participant.DisplayName);
        Assert.Null(result.GuestToken);
        Assert.Null(result.GuestTokenExpiresAt);
        VerifyTransaction(
            wishlistId,
            commits: true,
            cancellationToken: cancellationToken);
        VerifyValidGuest(
            participant.GuestSessionId.GetValueOrDefault(),
            cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                participant.GuestSessionId.GetValueOrDefault(),
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JoinAsync_WhenKnownGuestJoinsAnotherList_CreatesParticipantWithoutCookie()
    {
        // Arrange
        var participantId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var guestSessionId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = SetupValidGuest(
            guestSessionId,
            cancellationToken);
        SetupTransaction(
            wishlistId,
            ownerId,
            cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                guestSessionId,
                cancellationToken))
            .ReturnsAsync((WishlistParticipant?)null);
        SetupCapacity(
            wishlistId,
            4,
            cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.Add(It.Is<WishlistParticipant>(participant =>
                participant.Id == participantId &&
                participant.GuestDisplayName == string.Empty)));
        SetupSaveAndCommit(cancellationToken);

        // Act
        var result = await _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = participantId,
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                ShareLinkId = wishlistId,
                ShareSecret = "share-secret",
                GuestToken = token.Secret
            },
            cancellationToken);

        // Assert
        Assert.True(result.IsCreated);
        Assert.Equal(
            string.Empty,
            result.Participant.DisplayName);
        Assert.Null(result.GuestToken);
        VerifyTransaction(
            wishlistId,
            commits: true,
            cancellationToken: cancellationToken);
        VerifyValidGuest(
            guestSessionId,
            cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                guestSessionId,
                cancellationToken),
            Times.Once);
        _participantRepositoryMock.Verify(
            repository => repository.CountActiveAsync(
                wishlistId,
                _now,
                cancellationToken),
            Times.Once);
        _participantRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<WishlistParticipant>()),
            Times.Once);
        VerifySave(cancellationToken);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JoinAsync_WhenMemberParticipantExists_ReturnsCurrentProfileName()
    {
        // Arrange
        var participantId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var participant = WishlistParticipant.CreateMember(
            participantId,
            wishlistId,
            memberId);
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupTransaction(
            wishlistId,
            ownerId,
            cancellationToken);
        SetupMember(
            wishlistId,
            memberId,
            participant,
            cancellationToken);
        SetupCommit(cancellationToken);

        // Act
        var result = await _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = Guid.CreateVersion7(),
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                ShareLinkId = wishlistId,
                ShareSecret = "share-secret",
                MemberId = memberId
            },
            cancellationToken);

        // Assert
        Assert.False(result.IsCreated);
        Assert.Equal(
            participantId,
            result.Participant.Id);
        Assert.Equal(
            "Current profile",
            result.Participant.DisplayName);
        VerifyTransaction(
            wishlistId,
            commits: true,
            cancellationToken: cancellationToken);
        VerifyMember(
            wishlistId,
            memberId,
            cancellationToken);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JoinAsync_WhenGuestLogsIn_AttachesExistingParticipantToMember()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var guestSessionId = Guid.CreateVersion7();
        var guestParticipant = new WishlistParticipant(
            Guid.CreateVersion7(),
            wishlistId,
            guestSessionId,
            "Guest");
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = SetupValidGuest(
            guestSessionId,
            cancellationToken);
        SetupTransaction(
            wishlistId,
            ownerId,
            cancellationToken);
        SetupMember(
            wishlistId,
            memberId,
            null,
            cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                guestSessionId,
                cancellationToken))
            .ReturnsAsync(guestParticipant);
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetByParticipantForUpdateAsync(
                guestParticipant.Id,
                cancellationToken))
            .ReturnsAsync([]);
        SetupSaveAndCommit(cancellationToken);

        // Act
        var result = await _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = Guid.CreateVersion7(),
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                ShareLinkId = wishlistId,
                ShareSecret = "share-secret",
                MemberId = memberId,
                GuestToken = token.Secret
            },
            cancellationToken);

        // Assert
        Assert.False(result.IsCreated);
        Assert.Equal(
            guestParticipant.Id,
            result.Participant.Id);
        Assert.Equal(
            memberId,
            guestParticipant.MemberId);
        Assert.Null(guestParticipant.GuestSessionId);
        VerifyTransaction(
            wishlistId,
            commits: true,
            cancellationToken: cancellationToken);
        VerifyValidGuest(
            guestSessionId,
            cancellationToken);
        VerifyMember(
            wishlistId,
            memberId,
            cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                guestSessionId,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetByParticipantForUpdateAsync(
                guestParticipant.Id,
                cancellationToken),
            Times.Once);
        VerifySave(cancellationToken);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JoinAsync_WhenMemberAndGuestParticipantsExist_MergesReservationsAndRemovesGuestDuplicate()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var guestSessionId = Guid.CreateVersion7();
        var memberParticipant = WishlistParticipant.CreateMember(
            Guid.CreateVersion7(),
            wishlistId,
            memberId);
        var guestParticipant = new WishlistParticipant(
            Guid.CreateVersion7(),
            wishlistId,
            guestSessionId,
            "Guest");
        var sharedWishId = Guid.CreateVersion7();
        var guestSharedReservation = new GiftReservation(
            Guid.CreateVersion7(),
            wishlistId,
            sharedWishId,
            guestParticipant.Id,
            2);
        var guestUniqueReservation = new GiftReservation(
            Guid.CreateVersion7(),
            wishlistId,
            Guid.CreateVersion7(),
            guestParticipant.Id,
            1);
        var memberSharedReservation = new GiftReservation(
            Guid.CreateVersion7(),
            wishlistId,
            sharedWishId,
            memberParticipant.Id,
            3);
        var memberHistory = new GiftReservationHistory(
            memberSharedReservation.Id,
            memberId,
            wishlistId,
            "Birthday",
            sharedWishId,
            "Shared gift",
            memberSharedReservation.Quantity,
            _now.AddHours(-1),
            _now.AddHours(-1));
        var addedHistory = (GiftReservationHistory?)null;
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = SetupValidGuest(
            guestSessionId,
            cancellationToken);
        SetupTransaction(
            wishlistId,
            ownerId,
            cancellationToken);
        SetupMember(
            wishlistId,
            memberId,
            memberParticipant,
            cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                guestSessionId,
                cancellationToken))
            .ReturnsAsync(guestParticipant);
        _participantRepositoryMock
            .Setup(repository => repository.Remove(guestParticipant));
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetByParticipantForUpdateAsync(
                guestParticipant.Id,
                cancellationToken))
            .ReturnsAsync(
            [
                guestSharedReservation,
                guestUniqueReservation
            ]);
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetByParticipantForUpdateAsync(
                memberParticipant.Id,
                cancellationToken))
            .ReturnsAsync(
            [
                memberSharedReservation
            ]);
        _giftReservationRepositoryMock
            .Setup(repository => repository.Remove(guestSharedReservation));
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetHistoryForUpdateAsync(
                memberSharedReservation.Id,
                cancellationToken))
            .ReturnsAsync(memberHistory);
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetHistorySourceAsync(
                wishlistId,
                guestUniqueReservation.WishId,
                cancellationToken))
            .ReturnsAsync(new GiftReservationHistorySource(
                "Birthday",
                "Unique gift"));
        _giftReservationRepositoryMock
            .Setup(repository => repository.AddHistory(It.IsAny<GiftReservationHistory>()))
            .Callback<GiftReservationHistory>(history => addedHistory = history);
        SetupSaveAndCommit(cancellationToken);

        // Act
        var result = await _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = Guid.CreateVersion7(),
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                ShareLinkId = wishlistId,
                ShareSecret = "share-secret",
                MemberId = memberId,
                GuestToken = token.Secret
            },
            cancellationToken);

        // Assert
        Assert.False(result.IsCreated);
        Assert.Equal(
            memberParticipant.Id,
            result.Participant.Id);
        Assert.Equal(
            5,
            memberSharedReservation.Quantity);
        Assert.Equal(
            memberParticipant.Id,
            guestUniqueReservation.WishlistParticipantId);
        Assert.Equal(
            5,
            memberHistory.Quantity);
        Assert.Equal(
            _now,
            memberHistory.LastActivityAt);
        Assert.NotNull(addedHistory);
        Assert.Equal(
            guestUniqueReservation.Id,
            addedHistory.Id);
        Assert.Equal(
            memberId,
            addedHistory.MemberId);
        VerifyTransaction(
            wishlistId,
            commits: true,
            cancellationToken: cancellationToken);
        VerifyValidGuest(
            guestSessionId,
            cancellationToken);
        VerifyMember(
            wishlistId,
            memberId,
            cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                guestSessionId,
                cancellationToken),
            Times.Once);
        _participantRepositoryMock.Verify(
            repository => repository.Remove(guestParticipant),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetByParticipantForUpdateAsync(
                guestParticipant.Id,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetByParticipantForUpdateAsync(
                memberParticipant.Id,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.Remove(guestSharedReservation),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetHistoryForUpdateAsync(
                memberSharedReservation.Id,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetHistorySourceAsync(
                wishlistId,
                guestUniqueReservation.WishId,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.AddHistory(addedHistory),
            Times.Once);
        VerifySave(cancellationToken);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JoinAsync_WhenMergedMemberHistoryIsMissing_RecreatesHistory()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var guestSessionId = Guid.CreateVersion7();
        var memberParticipant = WishlistParticipant.CreateMember(
            Guid.CreateVersion7(),
            wishlistId,
            memberId);
        var guestParticipant = new WishlistParticipant(
            Guid.CreateVersion7(),
            wishlistId,
            guestSessionId,
            "Guest");
        var wishId = Guid.CreateVersion7();
        var guestReservation = new GiftReservation(
            Guid.CreateVersion7(),
            wishlistId,
            wishId,
            guestParticipant.Id,
            2);
        var memberReservation = new GiftReservation(
            Guid.CreateVersion7(),
            wishlistId,
            wishId,
            memberParticipant.Id,
            3);
        var addedHistory = (GiftReservationHistory?)null;
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = SetupValidGuest(
            guestSessionId,
            cancellationToken);
        SetupTransaction(
            wishlistId,
            ownerId,
            cancellationToken);
        SetupMember(
            wishlistId,
            memberId,
            memberParticipant,
            cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                guestSessionId,
                cancellationToken))
            .ReturnsAsync(guestParticipant);
        _participantRepositoryMock
            .Setup(repository => repository.Remove(guestParticipant));
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetByParticipantForUpdateAsync(
                guestParticipant.Id,
                cancellationToken))
            .ReturnsAsync(
            [
                guestReservation
            ]);
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetByParticipantForUpdateAsync(
                memberParticipant.Id,
                cancellationToken))
            .ReturnsAsync(
            [
                memberReservation
            ]);
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetHistoryForUpdateAsync(
                memberReservation.Id,
                cancellationToken))
            .ReturnsAsync((GiftReservationHistory?)null);
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetHistorySourceAsync(
                wishlistId,
                wishId,
                cancellationToken))
            .ReturnsAsync(new GiftReservationHistorySource(
                "Birthday",
                "Gift"));
        _giftReservationRepositoryMock
            .Setup(repository => repository.AddHistory(It.IsAny<GiftReservationHistory>()))
            .Callback<GiftReservationHistory>(history => addedHistory = history);
        _giftReservationRepositoryMock
            .Setup(repository => repository.Remove(guestReservation));
        SetupSaveAndCommit(cancellationToken);

        // Act
        var result = await _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = Guid.CreateVersion7(),
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                ShareLinkId = wishlistId,
                ShareSecret = "share-secret",
                MemberId = memberId,
                GuestToken = token.Secret
            },
            cancellationToken);

        // Assert
        Assert.False(result.IsCreated);
        Assert.Equal(
            5,
            memberReservation.Quantity);
        Assert.NotNull(addedHistory);
        Assert.Equal(
            memberReservation.Id,
            addedHistory.Id);
        Assert.Equal(
            5,
            addedHistory.Quantity);
        Assert.Equal(
            _now,
            addedHistory.LastActivityAt);
        VerifyTransaction(
            wishlistId,
            commits: true,
            cancellationToken: cancellationToken);
        VerifyValidGuest(
            guestSessionId,
            cancellationToken);
        VerifyMember(
            wishlistId,
            memberId,
            cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                guestSessionId,
                cancellationToken),
            Times.Once);
        _participantRepositoryMock.Verify(
            repository => repository.Remove(guestParticipant),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetByParticipantForUpdateAsync(
                guestParticipant.Id,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetByParticipantForUpdateAsync(
                memberParticipant.Id,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetHistoryForUpdateAsync(
                memberReservation.Id,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetHistorySourceAsync(
                wishlistId,
                wishId,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.AddHistory(addedHistory),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.Remove(guestReservation),
            Times.Once);
        VerifySave(cancellationToken);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JoinAsync_WhenAdoptedReservationSourceIsMissing_ThrowsWishNotFound()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var guestSessionId = Guid.CreateVersion7();
        var guestParticipant = new WishlistParticipant(
            Guid.CreateVersion7(),
            wishlistId,
            guestSessionId,
            "Guest");
        var reservation = new GiftReservation(
            Guid.CreateVersion7(),
            wishlistId,
            Guid.CreateVersion7(),
            guestParticipant.Id,
            2);
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = SetupValidGuest(
            guestSessionId,
            cancellationToken);
        SetupTransaction(
            wishlistId,
            ownerId,
            cancellationToken);
        SetupMember(
            wishlistId,
            memberId,
            null,
            cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                guestSessionId,
                cancellationToken))
            .ReturnsAsync(guestParticipant);
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetByParticipantForUpdateAsync(
                guestParticipant.Id,
                cancellationToken))
            .ReturnsAsync(
            [
                reservation
            ]);
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetHistorySourceAsync(
                wishlistId,
                reservation.WishId,
                cancellationToken))
            .ReturnsAsync((GiftReservationHistorySource?)null);

        // Act
        var action = () => _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = Guid.CreateVersion7(),
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                ShareLinkId = wishlistId,
                ShareSecret = "share-secret",
                MemberId = memberId,
                GuestToken = token.Secret
            },
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishNotFoundException>(action);
        VerifyTransaction(
            wishlistId,
            commits: false,
            cancellationToken: cancellationToken);
        VerifyValidGuest(
            guestSessionId,
            cancellationToken);
        VerifyMember(
            wishlistId,
            memberId,
            cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                guestSessionId,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetByParticipantForUpdateAsync(
                guestParticipant.Id,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetHistorySourceAsync(
                wishlistId,
                reservation.WishId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JoinAsync_WhenMemberIsNew_CreatesMemberParticipant()
    {
        // Arrange
        var participantId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupTransaction(
            wishlistId,
            ownerId,
            cancellationToken);
        SetupMember(
            wishlistId,
            memberId,
            null,
            cancellationToken);
        SetupCapacity(
            wishlistId,
            99,
            cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.Add(It.Is<WishlistParticipant>(participant =>
                participant.Id == participantId &&
                participant.MemberId == memberId)));
        SetupSaveAndCommit(cancellationToken);

        // Act
        var result = await _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = participantId,
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                ShareLinkId = wishlistId,
                ShareSecret = "share-secret",
                MemberId = memberId
            },
            cancellationToken);

        // Assert
        Assert.True(result.IsCreated);
        Assert.Equal(
            participantId,
            result.Participant.Id);
        VerifyTransaction(
            wishlistId,
            commits: true,
            cancellationToken: cancellationToken);
        VerifyMember(
            wishlistId,
            memberId,
            cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.CountActiveAsync(
                wishlistId,
                _now,
                cancellationToken),
            Times.Once);
        _participantRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<WishlistParticipant>()),
            Times.Once);
        VerifySave(cancellationToken);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JoinAsync_WhenMemberDoesNotExist_ThrowsInvalidSession()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupTransaction(
            wishlistId,
            Guid.CreateVersion7(),
            cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.GetMemberDisplayNameAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync((string?)null);

        // Act
        var action = () => _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = Guid.CreateVersion7(),
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                ShareLinkId = wishlistId,
                ShareSecret = "share-secret",
                MemberId = memberId
            },
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        VerifyTransaction(
            wishlistId,
            commits: false,
            cancellationToken: cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.GetMemberDisplayNameAsync(
                memberId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JoinAsync_WhenMemberOwnsWishlist_ThrowsConflict()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupTransaction(
            wishlistId,
            memberId,
            cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.GetMemberDisplayNameAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync("Owner");

        // Act
        var action = () => _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = Guid.CreateVersion7(),
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                ShareLinkId = wishlistId,
                ShareSecret = "share-secret",
                MemberId = memberId
            },
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistOwnerCannotJoinException>(action);
        VerifyTransaction(
            wishlistId,
            commits: false,
            cancellationToken: cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.GetMemberDisplayNameAsync(
                memberId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JoinAsync_WhenParticipantLimitIsReached_ThrowsConflict()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupTransaction(
            wishlistId,
            Guid.CreateVersion7(),
            cancellationToken);
        SetupCapacity(
            wishlistId,
            WishlistParticipantService.MaximumParticipantCount,
            cancellationToken);

        // Act
        var action = () => _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = Guid.CreateVersion7(),
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                ShareLinkId = wishlistId,
                ShareSecret = "share-secret",
                DisplayName = "Jenn"
            },
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishlistParticipantLimitReachedException>(action);
        VerifyTransaction(
            wishlistId,
            commits: false,
            cancellationToken: cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.CountActiveAsync(
                wishlistId,
                _now,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public async Task JoinAsync_WhenLockedShareLinkIsInvalid_ThrowsSharedWishlistNotFound(
        bool shareLinkExists,
        bool belongsToWishlist,
        bool secretIsValid)
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var secretHash = new byte[32];
        var cancellationToken = TestContext.Current.CancellationToken;
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(cancellationToken))
            .ReturnsAsync(_transactionMock.Object);
        _transactionFactoryMock
            .Setup(factory => factory.LockShareLinkAsync(
                shareLinkId,
                cancellationToken))
            .ReturnsAsync(shareLinkExists
                ? new WishlistShareLink(
                    shareLinkId,
                    belongsToWishlist
                        ? wishlistId
                        : Guid.CreateVersion7(),
                    secretHash,
                    "protected-secret")
                : null);

        if (shareLinkExists && belongsToWishlist)
        {
            _wishlistShareTokenServiceMock
                .Setup(service => service.Verify(
                    "share-secret",
                    It.Is<byte[]>(hash => hash.SequenceEqual(secretHash))))
                .Returns(secretIsValid);
        }

        // Act
        var action = () => _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = Guid.CreateVersion7(),
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = wishlistId,
                ShareLinkId = shareLinkId,
                ShareSecret = "share-secret",
                DisplayName = "Jenn"
            },
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<SharedWishlistNotFoundException>(action);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(cancellationToken),
            Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.LockShareLinkAsync(
                shareLinkId,
                cancellationToken),
            Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.LockWishlistAsync(
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _wishlistShareTokenServiceMock.Verify(
            service => service.Verify(
                "share-secret",
                It.IsAny<byte[]>()),
            shareLinkExists && belongsToWishlist
                ? Times.Once()
                : Times.Never());
        _transactionMock.Verify(
            transaction => transaction.CommitAsync(cancellationToken),
            Times.Never);
        _transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task JoinAsync_WhenTransactionCreationFails_TranslatesOnlyPostgreSqlFailures(
        bool isPostgreSqlFailure)
    {
        // Arrange
        var exception = isPostgreSqlFailure
            ? (Exception)new TimeoutException("Unavailable")
            : new InvalidOperationException("Unexpected");
        var cancellationToken = TestContext.Current.CancellationToken;
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(cancellationToken))
            .ThrowsAsync(exception);

        // Act
        var thrown = await Record.ExceptionAsync(() => _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = Guid.CreateVersion7(),
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = Guid.CreateVersion7(),
                ShareLinkId = Guid.CreateVersion7(),
                ShareSecret = "share-secret",
                DisplayName = "Jenn"
            },
            cancellationToken));

        // Assert
        Assert.IsType(
            isPostgreSqlFailure
                ? typeof(DependencyUnavailableException)
                : typeof(InvalidOperationException),
            thrown);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task JoinAsync_WhenTransactionOperationFails_DoesNotTranslateNonPostgreSqlFailure()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var completionSource = new TaskCompletionSource<WishlistShareLink?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeStartedSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeCompletionSource = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(cancellationToken))
            .ReturnsAsync(_transactionMock.Object);
        _transactionFactoryMock
            .Setup(factory => factory.LockShareLinkAsync(
                shareLinkId,
                cancellationToken))
            .Returns(completionSource.Task);
        _transactionMock
            .Setup(transaction => transaction.DisposeAsync())
            .Callback(() => disposeStartedSource.SetResult())
            .Returns(() => new ValueTask(disposeCompletionSource.Task));

        // Act
        var resultTask = _service.JoinAsync(
            new WishlistParticipantJoinRequest
            {
                ParticipantId = Guid.CreateVersion7(),
                GuestSessionId = Guid.CreateVersion7(),
                WishlistId = Guid.CreateVersion7(),
                ShareLinkId = shareLinkId,
                ShareSecret = "share-secret",
                DisplayName = "Jenn"
            },
            cancellationToken);
        completionSource.SetException(new InvalidOperationException("Unexpected"));
        await disposeStartedSource.Task.WaitAsync(cancellationToken);
        disposeCompletionSource.SetResult();

        // Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => resultTask);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(cancellationToken),
            Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.LockShareLinkAsync(
                shareLinkId,
                cancellationToken),
            Times.Once);
        _transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, WishlistParticipantLookupOutcome.MissingIdentity)]
    [InlineData("invalid", WishlistParticipantLookupOutcome.InvalidGuestSession)]
    public async Task GetCurrentAsync_WhenGuestIdentityIsMissingOrInvalid_ReturnsExpectedOutcome(
        string? guestToken,
        WishlistParticipantLookupOutcome expectedOutcome)
    {
        // Arrange
        // Act
        var result = await _service.GetCurrentAsync(
            Guid.CreateVersion7(),
            null,
            guestToken,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedOutcome,
            result.Outcome);
        Assert.Null(result.Participant);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetCurrentAsync_WhenMemberExists_ReturnsParticipationState(bool participantExists)
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var participant = participantExists
            ? WishlistParticipant.CreateMember(
                Guid.CreateVersion7(),
                wishlistId,
                memberId)
            : null;
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupMember(
            wishlistId,
            memberId,
            participant,
            cancellationToken);

        // Act
        var result = await _service.GetCurrentAsync(
            wishlistId,
            memberId,
            "ignored",
            cancellationToken);

        // Assert
        Assert.Equal(
            participantExists
                ? WishlistParticipantLookupOutcome.Found
                : WishlistParticipantLookupOutcome.NotJoined,
            result.Outcome);
        Assert.Equal(
            participant?.Id,
            result.Participant?.Id);
        VerifyMember(
            wishlistId,
            memberId,
            cancellationToken);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetCurrentAsync_WhenMemberDoesNotExist_ReturnsMemberNotFound()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _participantRepositoryMock
            .Setup(repository => repository.GetMemberDisplayNameAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync((string?)null);

        // Act
        var result = await _service.GetCurrentAsync(
            Guid.CreateVersion7(),
            memberId,
            null,
            cancellationToken);

        // Assert
        Assert.Equal(
            WishlistParticipantLookupOutcome.MemberNotFound,
            result.Outcome);
        _participantRepositoryMock.Verify(
            repository => repository.GetMemberDisplayNameAsync(
                memberId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetCurrentAsync_WhenGuestSessionIsValid_ReturnsParticipationState(
        bool participantExists)
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var guestSessionId = Guid.CreateVersion7();
        var participant = participantExists
            ? new WishlistParticipant(
                Guid.CreateVersion7(),
                wishlistId,
                guestSessionId,
                "Jenn")
            : null;
        var cancellationToken = TestContext.Current.CancellationToken;
        var token = SetupValidGuest(
            guestSessionId,
            cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                guestSessionId,
                cancellationToken))
            .ReturnsAsync(participant);

        // Act
        var result = await _service.GetCurrentAsync(
            wishlistId,
            null,
            token.Secret,
            cancellationToken);

        // Assert
        Assert.Equal(
            participantExists
                ? WishlistParticipantLookupOutcome.Found
                : WishlistParticipantLookupOutcome.NotJoined,
            result.Outcome);
        Assert.Equal(
            participant?.GuestDisplayName,
            result.Participant?.DisplayName);
        VerifyValidGuest(
            guestSessionId,
            cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.GetByGuestSessionForUpdateAsync(
                wishlistId,
                guestSessionId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("hash")]
    public async Task GetCurrentAsync_WhenPersistedGuestSessionIsInvalid_ReturnsInvalidSession(string scenario)
    {
        // Arrange
        var sessionId = Guid.CreateVersion7();
        var token = _tokenService.Create(sessionId);
        var cancellationToken = TestContext.Current.CancellationToken;
        var session = scenario switch
        {
            "missing" => null,
            "expired" => new GuestSession(
                sessionId,
                token.SecretHash,
                _now),
            _ => new GuestSession(
                sessionId,
                new byte[32],
                _now.AddDays(1))
        };
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                sessionId,
                cancellationToken))
            .ReturnsAsync(session);

        // Act
        var result = await _service.GetCurrentAsync(
            Guid.CreateVersion7(),
            null,
            token.Secret,
            cancellationToken);

        // Assert
        Assert.Equal(
            WishlistParticipantLookupOutcome.InvalidGuestSession,
            result.Outcome);
        _guestSessionRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                sessionId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetCurrentAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailable()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _participantRepositoryMock
            .Setup(repository => repository.GetMemberDisplayNameAsync(
                memberId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException("Unavailable"));

        // Act
        var action = () => _service.GetCurrentAsync(
            Guid.CreateVersion7(),
            memberId,
            null,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _participantRepositoryMock.Verify(
            repository => repository.GetMemberDisplayNameAsync(
                memberId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private GuestSessionToken SetupValidGuest(
        Guid guestSessionId,
        CancellationToken cancellationToken)
    {
        var token = _tokenService.Create(guestSessionId);
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                guestSessionId,
                cancellationToken))
            .ReturnsAsync(new GuestSession(
                guestSessionId,
                token.SecretHash,
                _now.AddDays(1)));

        return token;
    }

    private void SetupTransaction(
        Guid wishlistId,
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var secretHash = new byte[32];
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(cancellationToken))
            .ReturnsAsync(_transactionMock.Object);
        _transactionFactoryMock
            .Setup(factory => factory.LockShareLinkAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(new WishlistShareLink(
                wishlistId,
                wishlistId,
                secretHash,
                "protected-secret"));
        _wishlistShareTokenServiceMock
            .Setup(service => service.Verify(
                "share-secret",
                It.Is<byte[]>(hash => hash.SequenceEqual(secretHash))))
            .Returns(true);
        _transactionFactoryMock
            .Setup(factory => factory.LockWishlistAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(ownerId);
    }

    private void SetupMember(
        Guid wishlistId,
        Guid memberId,
        WishlistParticipant? participant,
        CancellationToken cancellationToken)
    {
        _participantRepositoryMock
            .Setup(repository => repository.GetMemberDisplayNameAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync("Current profile");
        _participantRepositoryMock
            .Setup(repository => repository.GetByMemberForUpdateAsync(
                wishlistId,
                memberId,
                cancellationToken))
            .ReturnsAsync(participant);
    }

    private void SetupCapacity(
        Guid wishlistId,
        int count,
        CancellationToken cancellationToken)
    {
        _participantRepositoryMock
            .Setup(repository => repository.CountActiveAsync(
                wishlistId,
                _now,
                cancellationToken))
            .ReturnsAsync(count);
    }

    private void SetupSaveAndCommit(CancellationToken cancellationToken)
    {
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);
        SetupCommit(cancellationToken);
    }

    private void SetupCommit(CancellationToken cancellationToken)
    {
        _transactionMock
            .Setup(transaction => transaction.CommitAsync(cancellationToken))
            .Returns(Task.CompletedTask);
    }

    private void VerifyTransaction(
        Guid wishlistId,
        bool commits,
        CancellationToken cancellationToken)
    {
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(cancellationToken),
            Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.LockShareLinkAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        _wishlistShareTokenServiceMock.Verify(
            service => service.Verify(
                "share-secret",
                It.IsAny<byte[]>()),
            Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.LockWishlistAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        _transactionMock.Verify(
            transaction => transaction.CommitAsync(cancellationToken),
            commits
                ? Times.Once()
                : Times.Never());
        _transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
    }

    private void VerifyValidGuest(
        Guid guestSessionId,
        CancellationToken cancellationToken)
    {
        _guestSessionRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                guestSessionId,
                cancellationToken),
            Times.Once);
    }

    private void VerifyMember(
        Guid wishlistId,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        _participantRepositoryMock.Verify(
            repository => repository.GetMemberDisplayNameAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _participantRepositoryMock.Verify(
            repository => repository.GetByMemberForUpdateAsync(
                wishlistId,
                memberId,
                cancellationToken),
            Times.Once);
    }

    private void VerifySave(CancellationToken cancellationToken)
    {
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
    }

    private void VerifyNoOtherCalls()
    {
        _participantRepositoryMock.VerifyNoOtherCalls();
        _guestSessionRepositoryMock.VerifyNoOtherCalls();
        _giftReservationRepositoryMock.VerifyNoOtherCalls();
        _transactionFactoryMock.VerifyNoOtherCalls();
        _transactionMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
        _wishlistShareTokenServiceMock.VerifyNoOtherCalls();
    }
}
