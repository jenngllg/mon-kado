using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class RefreshSessionServiceTests
{
    private const string RefreshToken = "session.refresh-secret";
    private readonly Mock<IAuthenticationSessionRepository> _sessionRepositoryMock;
    private readonly Mock<IRefreshTokenService> _refreshTokenServiceMock;
    private readonly RefreshSessionService _refreshSessionService;

    public RefreshSessionServiceTests()
    {
        _sessionRepositoryMock = new Mock<IAuthenticationSessionRepository>(MockBehavior.Strict);
        _refreshTokenServiceMock = new Mock<IRefreshTokenService>(MockBehavior.Strict);
        _refreshSessionService = new RefreshSessionService(
            _sessionRepositoryMock.Object,
            _refreshTokenServiceMock.Object,
            new FixedTimeProvider(new DateTimeOffset(
                2026,
                8,
                24,
                12,
                0,
                0,
                TimeSpan.Zero)));
    }

    [Fact]
    public async Task CreateAsync_WhenSessionIdentifierIsNotRequested_GeneratesVersion7Identifier()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var expectedHash = new byte[32];
        var refreshToken = new RefreshToken(
            RefreshToken,
            expectedHash);
        AuthenticationSession? addedSession = null;
        _refreshTokenServiceMock
            .Setup(service => service.Create(It.IsAny<Guid>()))
            .Returns(refreshToken);
        _sessionRepositoryMock
            .Setup(repository => repository.Add(It.IsAny<AuthenticationSession>()))
            .Callback<AuthenticationSession>(session => addedSession = session);

        // Act
        var result = await _refreshSessionService.CreateAsync(
            memberId,
            isPersistent: false,
            requestedSessionId: null,
            currentSessionId: null,
            cancellationToken);

        // Assert
        Assert.NotNull(addedSession);
        Assert.Equal(
            7,
            addedSession.Id.Version);
        Assert.Equal(
            memberId,
            addedSession.UserId);
        Assert.Same(
            expectedHash,
            addedSession.RefreshTokenHash);
        Assert.Equal(
            RefreshToken,
            result.RefreshToken);
        _refreshTokenServiceMock.Verify(
            service => service.Create(addedSession.Id),
            Times.Once);
        _sessionRepositoryMock.Verify(
            repository => repository.Add(addedSession),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _refreshTokenServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProveCurrentSessionAsync_WhenPostgreSqlIsUnavailable_ThrowsDependencyUnavailable()
    {
        // Arrange
        var sessionId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _refreshTokenServiceMock
            .Setup(service => service.TryGetSessionId(
                RefreshToken,
                out sessionId))
            .Returns(true);
        _sessionRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                sessionId,
                cancellationToken))
            .ThrowsAsync(new TimeoutException());

        // Act
        var action = () => _refreshSessionService.ProveCurrentSessionAsync(
            RefreshToken,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<DependencyUnavailableException>(action);
        Assert.IsType<TimeoutException>(exception.InnerException);
        _refreshTokenServiceMock.Verify(
            service => service.TryGetSessionId(
                RefreshToken,
                out sessionId),
            Times.Once);
        _sessionRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                sessionId,
                cancellationToken),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _refreshTokenServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProveCurrentSessionAsync_WhenRepositoryThrowsUnrelatedException_PropagatesException()
    {
        // Arrange
        var sessionId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var expectedException = new InvalidOperationException();
        _refreshTokenServiceMock
            .Setup(service => service.TryGetSessionId(
                RefreshToken,
                out sessionId))
            .Returns(true);
        _sessionRepositoryMock
            .Setup(repository => repository.GetByIdAsync(
                sessionId,
                cancellationToken))
            .ThrowsAsync(expectedException);

        // Act
        var action = () => _refreshSessionService.ProveCurrentSessionAsync(
            RefreshToken,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(action);
        Assert.Same(
            expectedException,
            exception);
        _refreshTokenServiceMock.Verify(
            service => service.TryGetSessionId(
                RefreshToken,
                out sessionId),
            Times.Once);
        _sessionRepositoryMock.Verify(
            repository => repository.GetByIdAsync(
                sessionId,
                cancellationToken),
            Times.Once);
        _sessionRepositoryMock.VerifyNoOtherCalls();
        _refreshTokenServiceMock.VerifyNoOtherCalls();
    }
}
