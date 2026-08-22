using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetCurrentSessionQueryHandlerTests
{
    private readonly GetCurrentSessionQueryHandler _handler;
    private readonly Mock<ICurrentSessionService> _currentSessionServiceMock;

    public GetCurrentSessionQueryHandlerTests()
    {
        _currentSessionServiceMock = new Mock<ICurrentSessionService>(MockBehavior.Strict);
        _handler = new GetCurrentSessionQueryHandler(
            _currentSessionServiceMock.Object,
            NullLogger<GetCurrentSessionQueryHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenMemberExists_ReturnsCurrentSession()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var currentSession = new CurrentSession(
            memberId,
            "jenn@example.fr",
            "Jenn",
            ["Member"]);
        var query = new GetCurrentSessionQuery(memberId);
        _currentSessionServiceMock
            .Setup(service => service.GetAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync(currentSession);

        // Act
        var result = await _handler.Handle(
            query,
            cancellationToken);

        // Assert
        Assert.Same(
            currentSession,
            result);
        _currentSessionServiceMock.Verify(
            service => service.GetAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _currentSessionServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenMemberDoesNotExist_ThrowsInvalidAuthenticationSessionException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var query = new GetCurrentSessionQuery(memberId);
        _currentSessionServiceMock
            .Setup(service => service.GetAsync(
                memberId,
                cancellationToken))
            .ReturnsAsync((CurrentSession?)null);

        // Act
        var action = () => _handler.Handle(
            query,
            cancellationToken);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        _currentSessionServiceMock.Verify(
            service => service.GetAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _currentSessionServiceMock.VerifyNoOtherCalls();
    }
}
