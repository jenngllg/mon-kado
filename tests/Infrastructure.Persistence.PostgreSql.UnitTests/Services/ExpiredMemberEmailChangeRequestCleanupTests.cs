using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class ExpiredMemberEmailChangeRequestCleanupTests
{
    private readonly ExpiredMemberEmailChangeRequestCleanup _cleanup;
    private readonly Mock<IMemberEmailChangeRequestRepository> _requestRepositoryMock;

    public ExpiredMemberEmailChangeRequestCleanupTests()
    {
        _requestRepositoryMock =
            new Mock<IMemberEmailChangeRequestRepository>(MockBehavior.Strict);
        _cleanup = new ExpiredMemberEmailChangeRequestCleanup(_requestRepositoryMock.Object);
    }

    [Fact]
    public async Task DeleteExpiredRequestsAsync_WhenCalled_UsesSevenDayCompletedRetention()
    {
        // Arrange
        var now = new DateTime(
            2026,
            8,
            22,
            20,
            0,
            0,
            DateTimeKind.Utc);
        var cancellationToken = TestContext.Current.CancellationToken;
        _requestRepositoryMock
            .Setup(repository => repository.DeleteExpiredOrCompletedAsync(
                now,
                now.AddDays(-7),
                500,
                cancellationToken))
            .ReturnsAsync(3);

        // Act
        var result = await _cleanup.DeleteExpiredRequestsAsync(
            now,
            500,
            cancellationToken);

        // Assert
        Assert.Equal(
            3,
            result);
        _requestRepositoryMock.Verify(
            repository => repository.DeleteExpiredOrCompletedAsync(
                now,
                now.AddDays(-7),
                500,
                cancellationToken),
            Times.Once);
        _requestRepositoryMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteExpiredRequestsAsync_WhenBatchSizeIsInvalid_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        // Act
        var action = () => _cleanup.DeleteExpiredRequestsAsync(
            DateTime.UtcNow,
            0,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(action);
        _requestRepositoryMock.VerifyNoOtherCalls();
    }
}
