using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetCurrentWishlistParticipantQueryHandlerTests
{
    private readonly GetCurrentWishlistParticipantQueryHandler _handler;
    private readonly Mock<IWishlistParticipantService> _participantServiceMock;
    private readonly Mock<IWishlistShareService> _shareServiceMock;

    public GetCurrentWishlistParticipantQueryHandlerTests()
    {
        _shareServiceMock = new Mock<IWishlistShareService>(MockBehavior.Strict);
        _participantServiceMock = new Mock<IWishlistParticipantService>(MockBehavior.Strict);
        _handler = new GetCurrentWishlistParticipantQueryHandler(
            _shareServiceMock.Object,
            _participantServiceMock.Object,
            NullLogger<GetCurrentWishlistParticipantQueryHandler>.Instance);
    }

    public static TheoryData<WishlistParticipantLookupOutcome, Type> Failures => new()
    {
        {
            WishlistParticipantLookupOutcome.MemberNotFound,
            typeof(InvalidAuthenticationSessionException)
        },
        {
            WishlistParticipantLookupOutcome.MissingIdentity,
            typeof(GuestSessionInvalidException)
        },
        {
            WishlistParticipantLookupOutcome.InvalidGuestSession,
            typeof(GuestSessionInvalidException)
        },
        {
            WishlistParticipantLookupOutcome.NotJoined,
            typeof(WishlistParticipantNotFoundException)
        },
        {
            WishlistParticipantLookupOutcome.Found,
            typeof(WishlistParticipantNotFoundException)
        }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Handle_WhenLookupFails_ThrowsExpectedException(
        WishlistParticipantLookupOutcome outcome,
        Type expectedExceptionType)
    {
        // Arrange
        var query = CreateQuery();
        var wishlist = CreateWishlist();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupValidShareLink(
            query,
            wishlist,
            cancellationToken);
        _participantServiceMock
            .Setup(service => service.GetCurrentAsync(
                wishlist.Id,
                query.MemberId,
                query.GuestToken,
                cancellationToken))
            .ReturnsAsync(new WishlistParticipantLookupResult(
                outcome,
                null));

        // Act
        var exception = await Record.ExceptionAsync(() => _handler.Handle(
            query,
            cancellationToken));

        // Assert
        Assert.IsType(
            expectedExceptionType,
            exception);
        VerifyCalls(
            query,
            wishlist,
            cancellationToken);
    }

    [Fact]
    public async Task Handle_WhenParticipantExists_ReturnsParticipant()
    {
        // Arrange
        var query = CreateQuery();
        var wishlist = CreateWishlist();
        var participant = new WishlistParticipantDetails(
            Guid.CreateVersion7(),
            "Jenn");
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupValidShareLink(
            query,
            wishlist,
            cancellationToken);
        _participantServiceMock
            .Setup(service => service.GetCurrentAsync(
                wishlist.Id,
                query.MemberId,
                query.GuestToken,
                cancellationToken))
            .ReturnsAsync(new WishlistParticipantLookupResult(
                WishlistParticipantLookupOutcome.Found,
                participant));

        // Act
        var result = await _handler.Handle(
            query,
            cancellationToken);

        // Assert
        Assert.Same(
            participant,
            result);
        VerifyCalls(
            query,
            wishlist,
            cancellationToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenShareLinkIsInvalid_ThrowsSharedWishlistNotFound(bool secretIsMissing)
    {
        // Arrange
        var query = CreateQuery(secretIsMissing
            ? null
            : "secret");
        var cancellationToken = TestContext.Current.CancellationToken;
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                query.ShareLinkId,
                query.Secret ?? string.Empty,
                cancellationToken))
            .ReturnsAsync((SharedWishlistDetails?)null);

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<SharedWishlistNotFoundException>(action);
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                query.ShareLinkId,
                query.Secret ?? string.Empty,
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
        _participantServiceMock.VerifyNoOtherCalls();
    }

    private static GetCurrentWishlistParticipantQuery CreateQuery(string? secret = "secret")
    {
        return new GetCurrentWishlistParticipantQuery(
            Guid.CreateVersion7(),
            secret,
            null,
            "guest");
    }

    private static SharedWishlistDetails CreateWishlist()
    {
        return new SharedWishlistDetails(
            Guid.CreateVersion7(),
            "Owner",
            "Birthday",
            WishlistOccasion.Birthday,
            null,
            null,
            []);
    }

    private void SetupValidShareLink(
        GetCurrentWishlistParticipantQuery query,
        SharedWishlistDetails wishlist,
        CancellationToken cancellationToken)
    {
        _shareServiceMock
            .Setup(service => service.GetSharedAsync(
                query.ShareLinkId,
                query.Secret ?? string.Empty,
                cancellationToken))
            .ReturnsAsync(wishlist);
    }

    private void VerifyCalls(
        GetCurrentWishlistParticipantQuery query,
        SharedWishlistDetails wishlist,
        CancellationToken cancellationToken)
    {
        _shareServiceMock.Verify(
            service => service.GetSharedAsync(
                query.ShareLinkId,
                query.Secret ?? string.Empty,
                cancellationToken),
            Times.Once);
        _participantServiceMock.Verify(
            service => service.GetCurrentAsync(
                wishlist.Id,
                query.MemberId,
                query.GuestToken,
                cancellationToken),
            Times.Once);
        _shareServiceMock.VerifyNoOtherCalls();
        _participantServiceMock.VerifyNoOtherCalls();
    }
}
