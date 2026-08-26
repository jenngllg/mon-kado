using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class ExpiredGuestSessionCleanupTests
{
    private readonly ExpiredGuestSessionCleanup _cleanup;
    private readonly Mock<IGuestSessionRepository> _repositoryMock;
    private readonly DateTime _now = new(
        2026,
        8,
        26,
        12,
        0,
        0,
        DateTimeKind.Utc);

    public ExpiredGuestSessionCleanupTests()
    {
        _repositoryMock = new Mock<IGuestSessionRepository>(MockBehavior.Strict);
        _cleanup = new ExpiredGuestSessionCleanup(
            _repositoryMock.Object,
            new FixedTimeProvider(_now));
    }

    [Fact]
    public async Task DeleteExpiredSessionsAsync_WhenCalled_UsesCurrentTimeAndBatchSize()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _repositoryMock
            .Setup(repository => repository.DeleteExpiredAsync(
                _now,
                500,
                cancellationToken))
            .ReturnsAsync(3);

        // Act
        var result = await _cleanup.DeleteExpiredSessionsAsync(
            500,
            cancellationToken);

        // Assert
        Assert.Equal(
            3,
            result);
        _repositoryMock.Verify(
            repository => repository.DeleteExpiredAsync(
                _now,
                500,
                cancellationToken),
            Times.Once);
        _repositoryMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteExpiredSessionsAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailable()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _repositoryMock
            .Setup(repository => repository.DeleteExpiredAsync(
                _now,
                500,
                cancellationToken))
            .ThrowsAsync(new TimeoutException("Unavailable"));

        // Act
        var action = () => _cleanup.DeleteExpiredSessionsAsync(
            500,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        _repositoryMock.Verify(
            repository => repository.DeleteExpiredAsync(
                _now,
                500,
                cancellationToken),
            Times.Once);
        _repositoryMock.VerifyNoOtherCalls();
    }
}
