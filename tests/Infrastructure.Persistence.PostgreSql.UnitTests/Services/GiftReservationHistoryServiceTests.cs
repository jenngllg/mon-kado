using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class GiftReservationHistoryServiceTests
{
    private readonly Mock<IGiftReservationRepository> _giftReservationRepositoryMock;
    private readonly GiftReservationHistoryService _historyService;

    public GiftReservationHistoryServiceTests()
    {
        _giftReservationRepositoryMock = new Mock<IGiftReservationRepository>(MockBehavior.Strict);
        _historyService = new GiftReservationHistoryService(_giftReservationRepositoryMock.Object);
    }

    [Fact]
    public async Task GetAsync_WhenMemberDoesNotExist_ReturnsNull()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _giftReservationRepositoryMock
            .Setup(repository => repository.MemberExistsAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync(false);

        // Act
        var result = await _historyService.GetAsync(
            memberId,
            1,
            20,
            null,
            cancellationToken);

        // Assert
        Assert.Null(result);
        _giftReservationRepositoryMock.Verify(
            repository => repository.MemberExistsAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(int.MaxValue, 1)]
    public async Task GetAsync_WhenRequestedPageContainsNoItems_ReturnsEmptyPage(
        int page,
        int totalCount)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        SetupMemberAndCount(
            memberId,
            totalCount,
            null,
            cancellationToken);

        // Act
        var result = await _historyService.GetAsync(
            memberId,
            page,
            20,
            null,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.Items);
        Assert.Equal(
            page,
            result.CurrentPage);
        Assert.Equal(
            20,
            result.PageSize);
        Assert.Equal(
            totalCount,
            result.TotalCount);
        VerifyMemberAndCount(
            memberId,
            null,
            cancellationToken);
        _giftReservationRepositoryMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenRequestedPageContainsItems_ReturnsRepositoryPage()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var expected = new[]
        {
            new GiftReservationHistoryDetails
            {
                Id = Guid.CreateVersion7()
            }
        };
        SetupMemberAndCount(
            memberId,
            6,
            GiftReservationHistoryStatus.Cancelled,
            cancellationToken);
        _giftReservationRepositoryMock
            .Setup(repository => repository.GetHistoryPageAsync(
                memberId,
                GiftReservationHistoryStatus.Cancelled,
                5,
                5,
                cancellationToken))
            .ReturnsAsync(expected);

        // Act
        var result = await _historyService.GetAsync(
            memberId,
            2,
            5,
            GiftReservationHistoryStatus.Cancelled,
            cancellationToken);

        // Assert
        Assert.NotNull(result);
        Assert.Same(
            expected,
            result.Items);
        Assert.Equal(
            6,
            result.TotalCount);
        VerifyMemberAndCount(
            memberId,
            GiftReservationHistoryStatus.Cancelled,
            cancellationToken);
        _giftReservationRepositoryMock.Verify(
            repository => repository.GetHistoryPageAsync(
                memberId,
                GiftReservationHistoryStatus.Cancelled,
                5,
                5,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _giftReservationRepositoryMock
            .Setup(repository => repository.MemberExistsAsync(
                memberId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _historyService.GetAsync(
            memberId,
            1,
            20,
            null,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _giftReservationRepositoryMock.Verify(
            repository => repository.MemberExistsAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.VerifyNoOtherCalls();
    }

    private void SetupMemberAndCount(
        Guid memberId,
        int totalCount,
        GiftReservationHistoryStatus? status,
        CancellationToken cancellationToken)
    {
        _giftReservationRepositoryMock
            .Setup(repository => repository.MemberExistsAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync(true);
        _giftReservationRepositoryMock
            .Setup(repository => repository.CountHistoryAsync(
                memberId,
                status,
                cancellationToken))
            .ReturnsAsync(totalCount);
    }

    private void VerifyMemberAndCount(
        Guid memberId,
        GiftReservationHistoryStatus? status,
        CancellationToken cancellationToken)
    {
        _giftReservationRepositoryMock.Verify(
            repository => repository.MemberExistsAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _giftReservationRepositoryMock.Verify(
            repository => repository.CountHistoryAsync(
                memberId,
                status,
                cancellationToken),
            Times.Once);
    }
}
