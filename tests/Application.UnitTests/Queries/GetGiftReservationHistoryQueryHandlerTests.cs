using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetGiftReservationHistoryQueryHandlerTests
{
    private readonly GetGiftReservationHistoryQueryHandler _handler;
    private readonly Mock<IGiftReservationHistoryService> _historyServiceMock;

    public GetGiftReservationHistoryQueryHandlerTests()
    {
        _historyServiceMock = new Mock<IGiftReservationHistoryService>(MockBehavior.Strict);
        _handler = new GetGiftReservationHistoryQueryHandler(
            _historyServiceMock.Object,
            NullLogger<GetGiftReservationHistoryQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("active")]
    [InlineData("cancelled")]
    [InlineData("unavailable")]
    public async Task Handle_WhenMemberExists_MapsStatusAndReturnsPage(string? status)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetGiftReservationHistoryQuery(
            memberId,
            2,
            5,
            status);
        var expectedStatus = status switch
        {
            "active" => GiftReservationHistoryStatus.Active,
            "cancelled" => GiftReservationHistoryStatus.Cancelled,
            "unavailable" => GiftReservationHistoryStatus.Unavailable,
            _ => (GiftReservationHistoryStatus?)null
        };
        var expected = new GiftReservationHistoryPage
        {
            CurrentPage = 2,
            PageSize = 5,
            TotalCount = 8
        };
        _historyServiceMock
            .Setup(service => service.GetAsync(
                memberId,
                2,
                5,
                expectedStatus,
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(
            query,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _historyServiceMock.Verify(
            service => service.GetAsync(
                memberId,
                2,
                5,
                expectedStatus,
                cancellationToken),
            Times.Once);
        _historyServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenPaginationIsAbsent_UsesDefaults()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetGiftReservationHistoryQuery(
            memberId,
            null,
            null,
            null);
        var expected = new GiftReservationHistoryPage
        {
            CurrentPage = GetGiftReservationHistoryQuery.DefaultPage,
            PageSize = GetGiftReservationHistoryQuery.DefaultPageSize
        };
        _historyServiceMock
            .Setup(service => service.GetAsync(
                memberId,
                GetGiftReservationHistoryQuery.DefaultPage,
                GetGiftReservationHistoryQuery.DefaultPageSize,
                null,
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _handler.Handle(
            query,
            cancellationToken);

        // Assert
        Assert.Same(
            expected,
            result);
        _historyServiceMock.Verify(
            service => service.GetAsync(
                memberId,
                GetGiftReservationHistoryQuery.DefaultPage,
                GetGiftReservationHistoryQuery.DefaultPageSize,
                null,
                cancellationToken),
            Times.Once);
        _historyServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenMemberDoesNotExist_ThrowsInvalidAuthenticationSessionException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetGiftReservationHistoryQuery(
            memberId,
            1,
            20,
            null);
        _historyServiceMock
            .Setup(service => service.GetAsync(
                memberId,
                1,
                20,
                null,
                cancellationToken))
            .ReturnsAsync((GiftReservationHistoryPage?)null);

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        _historyServiceMock.Verify(
            service => service.GetAsync(
                memberId,
                1,
                20,
                null,
                cancellationToken),
            Times.Once);
        _historyServiceMock.VerifyNoOtherCalls();
    }
}
