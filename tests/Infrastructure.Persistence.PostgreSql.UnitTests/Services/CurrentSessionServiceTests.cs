using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class CurrentSessionServiceTests
{
    private readonly CurrentSessionService _currentSessionService;
    private readonly Mock<IMemberRepository> _memberRepositoryMock;

    public CurrentSessionServiceTests()
    {
        _memberRepositoryMock = new Mock<IMemberRepository>(MockBehavior.Strict);
        _currentSessionService = new CurrentSessionService(_memberRepositoryMock.Object);
    }

    [Fact]
    public async Task GetAsync_WhenRepositoryReturnsSession_ReturnsCurrentSession()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var currentSession = new CurrentSession(
            memberId,
            "jenn@example.fr",
            "Jenn",
            ["Member"]);
        _memberRepositoryMock
            .Setup(repository => repository.GetCurrentSessionAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync(currentSession);

        // Act
        var result = await _currentSessionService.GetAsync(
            memberId,
            cancellationToken);

        // Assert
        Assert.Same(
            currentSession,
            result);
        _memberRepositoryMock.Verify(
            repository => repository.GetCurrentSessionAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _memberRepositoryMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenPostgreSqlTimesOut_ThrowsDependencyUnavailableException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _memberRepositoryMock
            .Setup(repository => repository.GetCurrentSessionAsync(
                memberId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _currentSessionService.GetAsync(
            memberId,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        Assert.IsType<TimeoutException>(exception.InnerException);
        _memberRepositoryMock.Verify(
            repository => repository.GetCurrentSessionAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _memberRepositoryMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetAsync_WhenRepositoryThrowsUnrelatedException_PropagatesException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var expectedException = new InvalidOperationException();
        _memberRepositoryMock
            .Setup(repository => repository.GetCurrentSessionAsync(
                memberId,
                cancellationToken))
            .ThrowsAsync(expectedException);

        // Act
        var action = () => _currentSessionService.GetAsync(
            memberId,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Same(
            expectedException,
            exception);
        _memberRepositoryMock.Verify(
            repository => repository.GetCurrentSessionAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _memberRepositoryMock.VerifyNoOtherCalls();
    }
}
