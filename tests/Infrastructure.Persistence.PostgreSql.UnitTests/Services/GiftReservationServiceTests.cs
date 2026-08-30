using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class GiftReservationServiceTests
{
    private readonly DateTime _now = new(
        2026,
        8,
        30,
        12,
        0,
        0,
        DateTimeKind.Utc);
    private readonly Mock<IGiftReservationRepository> _giftReservationRepositoryMock;
    private readonly Mock<IGuestSessionRepository> _guestSessionRepositoryMock;
    private readonly GuestSessionTokenService _guestSessionTokenService = new();
    private readonly Mock<IWishlistParticipantRepository> _participantRepositoryMock;
    private readonly GiftReservationService _service;
    private readonly Mock<IGiftReservationTransactionFactory> _transactionFactoryMock;
    private readonly Mock<IGiftReservationTransaction> _transactionMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<IWishlistShareTokenService> _wishlistShareTokenServiceMock;

    public GiftReservationServiceTests()
    {
        _giftReservationRepositoryMock = new Mock<IGiftReservationRepository>(MockBehavior.Strict);
        _participantRepositoryMock = new Mock<IWishlistParticipantRepository>(MockBehavior.Strict);
        _guestSessionRepositoryMock = new Mock<IGuestSessionRepository>(MockBehavior.Strict);
        _wishlistShareTokenServiceMock = new Mock<IWishlistShareTokenService>(MockBehavior.Strict);
        _transactionFactoryMock = new Mock<IGiftReservationTransactionFactory>(MockBehavior.Strict);
        _transactionMock = new Mock<IGiftReservationTransaction>(MockBehavior.Strict);
        _unitOfWorkMock = new Mock<IUnitOfWork>(MockBehavior.Strict);
        _transactionMock
            .Setup(transaction => transaction.DisposeAsync())
            .Returns(ValueTask.CompletedTask);
        _service = new GiftReservationService(
            _giftReservationRepositoryMock.Object,
            _participantRepositoryMock.Object,
            _guestSessionRepositoryMock.Object,
            _guestSessionTokenService,
            _wishlistShareTokenServiceMock.Object,
            _transactionFactoryMock.Object,
            _unitOfWorkMock.Object,
            new FixedTimeProvider(_now));
    }

    [Fact]
    public async Task GetAsync_WhenReservationExists_ReturnsDetails()
    {
        // Arrange
        var reservation = CreateReservation(2);
        var cancellationToken = TestContext.Current.CancellationToken;
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetAsync(
                reservation.WishlistId,
                reservation.WishId,
                reservation.WishlistParticipantId,
                cancellationToken))
            .ReturnsAsync(reservation);

        // Act
        var result = await _service.GetAsync(
            reservation.WishlistId,
            reservation.WishId,
            reservation.WishlistParticipantId,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(
            reservation.Id,
            result.Id);
        Assert.Equal(
            reservation.WishId,
            result.WishId);
        Assert.Equal(
            2,
            result.Quantity);
        Assert.Equal(
            reservation.CreatedAt,
            result.CreatedAt);
        Assert.Equal(
            reservation.UpdatedAt,
            result.UpdatedAt);
        Assert.Equal(
            reservation.Version,
            result.Version);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetAsync(
                reservation.WishlistId,
                reservation.WishId,
                reservation.WishlistParticipantId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenReservationDoesNotExist_ReturnsNull()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var participantId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetAsync(
                wishlistId,
                wishId,
                participantId,
                cancellationToken))
            .ReturnsAsync((GiftReservation?)null);

        // Act
        var result = await _service.GetAsync(
            wishlistId,
            wishId,
            participantId,
            cancellationToken);

        // Assert
        Assert.Null(result);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetAsync(
                wishlistId,
                wishId,
                participantId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailable()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var participantId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetAsync(
                wishlistId,
                wishId,
                participantId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _service.GetAsync(
            wishlistId,
            wishId,
            participantId,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetAsync(
                wishlistId,
                wishId,
                participantId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetQuantitiesAsync_WhenPostgreSqlResponds_ReturnsQuantities()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var participantId = Guid.CreateVersion7();
        IReadOnlyDictionary<Guid, int> expected = new Dictionary<Guid, int>
        {
            [Guid.CreateVersion7()] = 2
        };
        var cancellationToken = TestContext.Current.CancellationToken;
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetQuantitiesAsync(
                wishlistId,
                participantId,
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.GetQuantitiesAsync(
            wishlistId,
            participantId,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetQuantitiesAsync(
                wishlistId,
                participantId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetQuantitiesAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailable()
    {
        // Arrange
        var wishlistId = Guid.CreateVersion7();
        var participantId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetQuantitiesAsync(
                wishlistId,
                participantId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _service.GetQuantitiesAsync(
            wishlistId,
            participantId,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetQuantitiesAsync(
                wishlistId,
                participantId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpsertAsync_WhenMemberCreatesAvailableReservation_ReturnsCreatedReservation()
    {
        // Arrange
        var request = CreateMemberRequest(2);
        var participant = WishlistParticipant.CreateMember(
            Guid.CreateVersion7(),
            request.WishlistId,
            request.MemberId.GetValueOrDefault());
        var wish = CreateWish(
            request,
            3);
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupMemberMutation(
            request,
            participant,
            wish,
            currentReservation: null,
            totalQuantity: 0,
            cancellationToken);
        GiftReservation? addedReservation = null;
        _giftReservationRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<GiftReservation>()))
            .Callback<GiftReservation>(reservation => addedReservation = reservation);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);
        _transactionMock
            .Setup(transaction => transaction.CommitAsync(cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.UpsertAsync(
            request,
            cancellationToken);

        // Assert
        Assert.True(result.IsCreated);
        Assert.NotNull(addedReservation);
        Assert.Equal(
            request.ReservationId,
            result.Reservation.Id);
        Assert.Equal(
            2,
            result.Reservation.Quantity);
        VerifyMemberMutation(
            request,
            participant,
            wish,
            currentReservation: null,
            cancellationToken);
        _giftReservationRepositoryMock.Verify(
            repository => repository.Add(addedReservation),
            Times.Once);
        VerifySaveAndCommit(cancellationToken);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpsertAsync_WhenGuestReducesOverreservedReservation_ReplacesQuantity()
    {
        // Arrange
        var request = CreateGuestRequest(3);
        var participant = new WishlistParticipant(
            Guid.CreateVersion7(),
            request.WishlistId,
            Guid.CreateVersion7(),
            "Guest");
        var currentReservation = new GiftReservation(
            Guid.CreateVersion7(),
            request.WishlistId,
            request.WishId,
            participant.Id,
            5);
        var wish = CreateWish(
            request,
            2);
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupGuestMutation(
            request,
            participant,
            wish,
            currentReservation,
            totalQuantity: 8,
            cancellationToken);
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ReturnsAsync(1);
        _transactionMock
            .Setup(transaction => transaction.CommitAsync(cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.UpsertAsync(
            request,
            cancellationToken);

        // Assert
        Assert.False(result.IsCreated);
        Assert.Equal(
            3,
            currentReservation.Quantity);
        VerifyGuestMutation(
            request,
            participant,
            wish,
            currentReservation,
            cancellationToken);
        VerifySaveAndCommit(cancellationToken);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpsertAsync_WhenQuantityIsUnchanged_DoesNotSave()
    {
        // Arrange
        var request = CreateMemberRequest(
            2,
            0);
        var participant = WishlistParticipant.CreateMember(
            Guid.CreateVersion7(),
            request.WishlistId,
            request.MemberId.GetValueOrDefault());
        var currentReservation = new GiftReservation(
            Guid.CreateVersion7(),
            request.WishlistId,
            request.WishId,
            participant.Id,
            2);
        var wish = CreateWish(
            request,
            3);
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupMemberMutation(
            request,
            participant,
            wish,
            currentReservation,
            totalQuantity: 2,
            cancellationToken);
        _transactionMock
            .Setup(transaction => transaction.CommitAsync(cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _service.UpsertAsync(
            request,
            cancellationToken);

        // Assert
        Assert.False(result.IsCreated);
        Assert.Equal(
            currentReservation.Id,
            result.Reservation.Id);
        VerifyMemberMutation(
            request,
            participant,
            wish,
            currentReservation,
            cancellationToken);
        _transactionMock.Verify(
            transaction => transaction.CommitAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpsertAsync_WhenRequestedIncreaseExceedsAvailableQuantity_ThrowsQuantityUnavailable()
    {
        // Arrange
        var request = CreateMemberRequest(
            3,
            0);
        var participant = WishlistParticipant.CreateMember(
            Guid.CreateVersion7(),
            request.WishlistId,
            request.MemberId.GetValueOrDefault());
        var currentReservation = new GiftReservation(
            Guid.CreateVersion7(),
            request.WishlistId,
            request.WishId,
            participant.Id,
            1);
        var wish = CreateWish(
            request,
            3);
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupMemberMutation(
            request,
            participant,
            wish,
            currentReservation,
            totalQuantity: 2,
            cancellationToken);

        // Act
        var action = () => _service.UpsertAsync(
            request,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<GiftReservationQuantityUnavailableException>(action);
        VerifyMemberMutation(
            request,
            participant,
            wish,
            currentReservation,
            cancellationToken);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpsertAsync_WhenWishDoesNotExist_ThrowsWishNotFound()
    {
        // Arrange
        var request = CreateMemberRequest(1);
        var participant = WishlistParticipant.CreateMember(
            Guid.CreateVersion7(),
            request.WishlistId,
            request.MemberId.GetValueOrDefault());
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupShare(
            request,
            cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.GetMemberDisplayNameAsync(
                request.MemberId.GetValueOrDefault(),
                cancellationToken))
            .ReturnsAsync("Jenn");
        _participantRepositoryMock
            .Setup(repository => repository.GetByMemberForUpdateAsync(
                request.WishlistId,
                request.MemberId.GetValueOrDefault(),
                cancellationToken))
            .ReturnsAsync(participant);
        _transactionFactoryMock
            .Setup(factory => factory.LockWishAsync(
                request.WishlistId,
                request.WishId,
                cancellationToken))
            .ReturnsAsync((Wish?)null);

        // Act
        var action = () => _service.UpsertAsync(
            request,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishNotFoundException>(action);
        VerifyShare(
            request,
            cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.GetMemberDisplayNameAsync(
                request.MemberId.GetValueOrDefault(),
                cancellationToken),
            Times.Once);
        _participantRepositoryMock.Verify(
            repository => repository.GetByMemberForUpdateAsync(
                request.WishlistId,
                request.MemberId.GetValueOrDefault(),
                cancellationToken),
            Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.LockWishAsync(
                request.WishlistId,
                request.WishId,
                cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(true, false, null)]
    [InlineData(true, true, 1)]
    public async Task UpsertAsync_WhenVersionDoesNotApply_ThrowsVersionConflictOrPreconditionRequired(
        bool reservationExists,
        bool versionIsPresent,
        int? expectedVersion)
    {
        // Arrange
        var request = CreateMemberRequest(
            1,
            versionIsPresent
                ? (uint?)expectedVersion
                : null);
        var participant = WishlistParticipant.CreateMember(
            Guid.CreateVersion7(),
            request.WishlistId,
            request.MemberId.GetValueOrDefault());
        var reservation = reservationExists
            ? new GiftReservation(
                Guid.CreateVersion7(),
                request.WishlistId,
                request.WishId,
                participant.Id,
                1)
            : null;
        var wish = CreateWish(
            request,
            3);
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupMemberMutationUntilVersion(
            request,
            participant,
            wish,
            reservation,
            cancellationToken);

        // Act
        var thrown = await Record.ExceptionAsync(() => _service.UpsertAsync(
            request,
            cancellationToken));

        // Assert
        Assert.IsType(
            reservationExists && !versionIsPresent
                ? typeof(PreconditionRequiredException)
                : typeof(GiftReservationVersionConflictException),
            thrown);
        VerifyMemberMutationUntilVersion(
            request,
            participant,
            wish,
            cancellationToken);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task UpsertAsync_WhenShareLinkIsInvalid_ThrowsSharedWishlistNotFound(int scenario)
    {
        // Arrange
        var request = CreateMemberRequest(1);
        var cancellationToken = TestContext.Current.CancellationToken;
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(cancellationToken))
            .ReturnsAsync(_transactionMock.Object);
        var shareLink = scenario == 0
            ? null
            : CreateShareLink(
                request,
                scenario == 1
                    ? Guid.CreateVersion7()
                    : request.WishlistId);
        _transactionFactoryMock
            .Setup(factory => factory.LockShareLinkAsync(
                request.ShareLinkId,
                cancellationToken))
            .ReturnsAsync(shareLink);

        if (scenario == 2 && shareLink is not null)
        {
            _wishlistShareTokenServiceMock
                .Setup(service => service.Verify(
                    request.ShareSecret,
                    shareLink.SecretHash))
                .Returns(false);
        }

        // Act
        var action = () => _service.UpsertAsync(
            request,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<SharedWishlistNotFoundException>(action);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(cancellationToken),
            Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.LockShareLinkAsync(
                request.ShareLinkId,
                cancellationToken),
            Times.Once);

        if (scenario == 2 && shareLink is not null)
        {
            _wishlistShareTokenServiceMock.Verify(
                service => service.Verify(
                    request.ShareSecret,
                    shareLink.SecretHash),
                Times.Once);
        }

        _transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task UpsertAsync_WhenMemberCannotBeResolved_ThrowsExpectedException(bool memberExists)
    {
        // Arrange
        var request = CreateMemberRequest(1);
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupShare(request, cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.GetMemberDisplayNameAsync(
                request.MemberId.GetValueOrDefault(),
                cancellationToken))
            .ReturnsAsync(memberExists
                ? "Jenn"
                : null);

        if (memberExists)
        {
            _participantRepositoryMock
                .Setup(repository => repository.GetByMemberForUpdateAsync(
                    request.WishlistId,
                    request.MemberId.GetValueOrDefault(),
                    cancellationToken))
                .ReturnsAsync((WishlistParticipant?)null);
        }

        // Act
        var thrown = await Record.ExceptionAsync(() => _service.UpsertAsync(
            request,
            cancellationToken));

        // Assert
        Assert.IsType(
            memberExists
                ? typeof(WishlistParticipantNotFoundException)
                : typeof(InvalidAuthenticationSessionException),
            thrown);
        VerifyShare(request, cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.GetMemberDisplayNameAsync(
                request.MemberId.GetValueOrDefault(),
                cancellationToken),
            Times.Once);

        if (memberExists)
        {
            _participantRepositoryMock.Verify(
                repository => repository.GetByMemberForUpdateAsync(
                    request.WishlistId,
                    request.MemberId.GetValueOrDefault(),
                    cancellationToken),
                Times.Once);
        }

        _transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task UpsertAsync_WhenGuestCannotBeResolved_ThrowsExpectedException(int scenario)
    {
        // Arrange
        var request = CreateGuestRequest(
            1,
            scenario == 0
                ? "invalid"
                : null,
            omitGuestToken: scenario == -1);
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupShare(request, cancellationToken);
        Guid guestSessionId = default;
        GuestSession? guestSession = null;

        if (scenario > 0)
        {
            _ = _guestSessionTokenService.TryParse(
                request.GuestToken ?? string.Empty,
                out guestSessionId,
                out _);
            guestSession = scenario == 1
                ? null
                : new GuestSession(
                    guestSessionId,
                    scenario == 3
                        ? [1]
                        : CreateGuestTokenHash(request.GuestToken),
                    scenario == 2
                        ? _now
                        : _now.AddHours(1));
            _guestSessionRepositoryMock
                .Setup(repository => repository.GetByIdAsync(
                    guestSessionId,
                    cancellationToken))
                .ReturnsAsync(guestSession);

            if (scenario == 4)
            {
                _participantRepositoryMock
                    .Setup(repository => repository.GetByGuestSessionForUpdateAsync(
                        request.WishlistId,
                        guestSessionId,
                        cancellationToken))
                    .ReturnsAsync((WishlistParticipant?)null);
            }
        }
        // Act
        var thrown = await Record.ExceptionAsync(() => _service.UpsertAsync(
            request,
            cancellationToken));

        // Assert
        Assert.IsType(
            scenario == 4
                ? typeof(WishlistParticipantNotFoundException)
                : typeof(GuestSessionInvalidException),
            thrown);
        VerifyShare(request, cancellationToken);

        if (scenario > 0)
        {
            _guestSessionRepositoryMock.Verify(
                repository => repository.GetByIdAsync(
                    guestSessionId,
                    cancellationToken),
                Times.Once);
        }

        if (scenario == 4)
        {
            _participantRepositoryMock.Verify(
                repository => repository.GetByGuestSessionForUpdateAsync(
                    request.WishlistId,
                    guestSessionId,
                    cancellationToken),
                Times.Once);
        }

        _transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpsertAsync_WhenSaveDetectsConcurrency_ThrowsVersionConflict()
    {
        // Arrange
        var request = CreateMemberRequest(1);
        var participant = WishlistParticipant.CreateMember(
            Guid.CreateVersion7(),
            request.WishlistId,
            request.MemberId.GetValueOrDefault());
        var wish = CreateWish(
            request,
            2);
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupMemberMutation(
            request,
            participant,
            wish,
            currentReservation: null,
            totalQuantity: 0,
            cancellationToken);
        _giftReservationRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<GiftReservation>()));
        _unitOfWorkMock
            .Setup(unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken))
            .ThrowsAsync(new DbUpdateConcurrencyException());

        // Act
        var action = () => _service.UpsertAsync(
            request,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<GiftReservationVersionConflictException>(action);
        VerifyMemberMutation(
            request,
            participant,
            wish,
            currentReservation: null,
            cancellationToken);
        _giftReservationRepositoryMock.Verify(
            repository => repository.Add(It.IsAny<GiftReservation>()),
            Times.Once);
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpsertAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailable()
    {
        // Arrange
        var request = CreateMemberRequest(1);
        var cancellationToken = TestContext.Current.CancellationToken;
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _service.UpsertAsync(
            request,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpsertAsync_WhenUnexpectedFailureOccurs_RethrowsException()
    {
        // Arrange
        var request = CreateMemberRequest(1);
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new InvalidOperationException("Unexpected failure");
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(cancellationToken))
            .ThrowsAsync(expected);

        // Act
        var exception = await Record.ExceptionAsync(() => _service.UpsertAsync(
            request,
            cancellationToken));

        // Assert
        Assert.Same(
            expected,
            exception);
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(cancellationToken),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private void SetupMemberMutation(
        GiftReservationMutationRequest request,
        WishlistParticipant participant,
        Wish wish,
        GiftReservation? currentReservation,
        int totalQuantity,
        CancellationToken cancellationToken)
    {
        SetupMemberMutationUntilVersion(
            request,
            participant,
            wish,
            currentReservation,
            cancellationToken);
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetTotalQuantityAsync(
                request.WishId,
                cancellationToken))
            .ReturnsAsync(totalQuantity);
    }

    private void SetupMemberMutationUntilVersion(
        GiftReservationMutationRequest request,
        WishlistParticipant participant,
        Wish wish,
        GiftReservation? currentReservation,
        CancellationToken cancellationToken)
    {
        SetupShare(request, cancellationToken);
        _participantRepositoryMock
            .Setup(repository => repository.GetMemberDisplayNameAsync(
                request.MemberId.GetValueOrDefault(),
                cancellationToken))
            .ReturnsAsync("Jenn");
        _participantRepositoryMock
            .Setup(repository => repository.GetByMemberForUpdateAsync(
                request.WishlistId,
                request.MemberId.GetValueOrDefault(),
                cancellationToken))
            .ReturnsAsync(participant);
        _transactionFactoryMock
            .Setup(factory => factory.LockWishAsync(
                request.WishlistId,
                request.WishId,
                cancellationToken))
            .ReturnsAsync(wish);
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetForUpdateAsync(
                request.WishlistId,
                request.WishId,
                participant.Id,
                cancellationToken))
            .ReturnsAsync(currentReservation);
    }

    private void SetupGuestMutation(
        GiftReservationMutationRequest request,
        WishlistParticipant participant,
        Wish wish,
        GiftReservation currentReservation,
        int totalQuantity,
        CancellationToken cancellationToken)
    {
        SetupShare(request, cancellationToken);
        _ = _guestSessionTokenService.TryParse(
            request.GuestToken ?? string.Empty,
            out var guestSessionId,
            out _);
        var guestSession = new GuestSession(
            guestSessionId,
            CreateGuestTokenHash(request.GuestToken),
            _now.AddHours(1));
        _guestSessionRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                guestSessionId,
                cancellationToken))
            .ReturnsAsync(guestSession);
        _participantRepositoryMock
            .Setup(repository => repository.GetByGuestSessionForUpdateAsync(
                request.WishlistId,
                guestSessionId,
                cancellationToken))
            .ReturnsAsync(participant);
        _transactionFactoryMock
            .Setup(factory => factory.LockWishAsync(
                request.WishlistId,
                request.WishId,
                cancellationToken))
            .ReturnsAsync(wish);
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetForUpdateAsync(
                request.WishlistId,
                request.WishId,
                participant.Id,
                cancellationToken))
            .ReturnsAsync(currentReservation);
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetTotalQuantityAsync(
                request.WishId,
                cancellationToken))
            .ReturnsAsync(totalQuantity);
    }

    private void SetupShare(
        GiftReservationMutationRequest request,
        CancellationToken cancellationToken)
    {
        var shareLink = CreateShareLink(
            request,
            request.WishlistId);
        _transactionFactoryMock
            .Setup(factory => factory.BeginAsync(cancellationToken))
            .ReturnsAsync(_transactionMock.Object);
        _transactionFactoryMock
            .Setup(factory => factory.LockShareLinkAsync(
                request.ShareLinkId,
                cancellationToken))
            .ReturnsAsync(shareLink);
        _wishlistShareTokenServiceMock
            .Setup(service => service.Verify(
                request.ShareSecret,
                shareLink.SecretHash))
            .Returns(true);
    }

    private void VerifyMemberMutation(
        GiftReservationMutationRequest request,
        WishlistParticipant participant,
        Wish wish,
        GiftReservation? currentReservation,
        CancellationToken cancellationToken)
    {
        VerifyMemberMutationUntilVersion(
            request,
            participant,
            wish,
            cancellationToken);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetTotalQuantityAsync(
                request.WishId,
                cancellationToken),
            Times.Once);
    }

    private void VerifyMemberMutationUntilVersion(
        GiftReservationMutationRequest request,
        WishlistParticipant participant,
        Wish wish,
        CancellationToken cancellationToken)
    {
        VerifyShare(request, cancellationToken);
        _participantRepositoryMock.Verify(
            repository => repository.GetMemberDisplayNameAsync(
                request.MemberId.GetValueOrDefault(),
                cancellationToken),
            Times.Once);
        _participantRepositoryMock.Verify(
            repository => repository.GetByMemberForUpdateAsync(
                request.WishlistId,
                request.MemberId.GetValueOrDefault(),
                cancellationToken),
            Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.LockWishAsync(
                request.WishlistId,
                request.WishId,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetForUpdateAsync(
                request.WishlistId,
                request.WishId,
                participant.Id,
                cancellationToken),
            Times.Once);
        _ = wish;
    }

    private void VerifyGuestMutation(
        GiftReservationMutationRequest request,
        WishlistParticipant participant,
        Wish wish,
        GiftReservation currentReservation,
        CancellationToken cancellationToken)
    {
        VerifyShare(request, cancellationToken);
        _ = _guestSessionTokenService.TryParse(
            request.GuestToken ?? string.Empty,
            out var guestSessionId,
            out _);
        _guestSessionRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                guestSessionId,
                cancellationToken),
            Times.Once);
        _participantRepositoryMock.Verify(
            repository => repository.GetByGuestSessionForUpdateAsync(
                request.WishlistId,
                guestSessionId,
                cancellationToken),
            Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.LockWishAsync(
                request.WishlistId,
                request.WishId,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetForUpdateAsync(
                request.WishlistId,
                request.WishId,
                participant.Id,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetTotalQuantityAsync(
                request.WishId,
                cancellationToken),
            Times.Once);
        _ = wish;
        _ = currentReservation;
    }

    private void VerifyShare(
        GiftReservationMutationRequest request,
        CancellationToken cancellationToken)
    {
        _transactionFactoryMock.Verify(
            factory => factory.BeginAsync(cancellationToken),
            Times.Once);
        _transactionFactoryMock.Verify(
            factory => factory.LockShareLinkAsync(
                request.ShareLinkId,
                cancellationToken),
            Times.Once);
        _wishlistShareTokenServiceMock.Verify(
            service => service.Verify(
                request.ShareSecret,
                It.IsAny<byte[]>()),
            Times.Once);
        _transactionMock.Verify(
            transaction => transaction.DisposeAsync(),
            Times.Once);
    }

    private void VerifySaveAndCommit(CancellationToken cancellationToken)
    {
        _unitOfWorkMock.Verify(
            unitOfWork => unitOfWork.SaveChangesAsync(cancellationToken),
            Times.Once);
        _transactionMock.Verify(
            transaction => transaction.CommitAsync(cancellationToken),
            Times.Once);
    }

    private void VerifyNoOtherCalls()
    {
        _giftReservationRepositoryMock.VerifyNoOtherCalls();
        _participantRepositoryMock.VerifyNoOtherCalls();
        _guestSessionRepositoryMock.VerifyNoOtherCalls();
        _wishlistShareTokenServiceMock.VerifyNoOtherCalls();
        _transactionFactoryMock.VerifyNoOtherCalls();
        _transactionMock.VerifyNoOtherCalls();
        _unitOfWorkMock.VerifyNoOtherCalls();
    }

    private static GiftReservationMutationRequest CreateMemberRequest(
        int quantity,
        uint? expectedVersion = null)
    {
        return new GiftReservationMutationRequest
        {
            ReservationId = Guid.CreateVersion7(),
            ShareLinkId = Guid.CreateVersion7(),
            ShareSecret = "secret",
            WishlistId = Guid.CreateVersion7(),
            WishId = Guid.CreateVersion7(),
            MemberId = Guid.CreateVersion7(),
            Quantity = quantity,
            ExpectedVersion = expectedVersion
        };
    }

    private GiftReservationMutationRequest CreateGuestRequest(
        int quantity,
        string? guestTokenOverride = null,
        bool omitGuestToken = false)
    {
        var guestToken = _guestSessionTokenService.Create(Guid.CreateVersion7());

        return new GiftReservationMutationRequest
        {
            ReservationId = Guid.CreateVersion7(),
            ShareLinkId = Guid.CreateVersion7(),
            ShareSecret = "secret",
            WishlistId = Guid.CreateVersion7(),
            WishId = Guid.CreateVersion7(),
            GuestToken = omitGuestToken
                ? null
                : guestTokenOverride ?? guestToken.Secret,
            Quantity = quantity,
            ExpectedVersion = 0
        };
    }

    private static GiftReservation CreateReservation(int quantity)
    {
        return new GiftReservation(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            quantity);
    }

    private static WishlistShareLink CreateShareLink(
        GiftReservationMutationRequest request,
        Guid wishlistId)
    {
        return new WishlistShareLink(
            request.ShareLinkId,
            wishlistId,
            [1],
            "protected");
    }

    private static Wish CreateWish(
        GiftReservationMutationRequest request,
        int quantity)
    {
        return new Wish(
            request.WishId,
            request.WishlistId,
            "Gift",
            null,
            null,
            null,
            1,
            quantity);
    }

    private byte[] CreateGuestTokenHash(string? guestToken)
    {
        _ = _guestSessionTokenService.TryParse(
            guestToken ?? string.Empty,
            out _,
            out var hash);

        return hash;
    }
}
