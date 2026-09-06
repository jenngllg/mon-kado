using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class RequestMemberAccountDeletionCommandHandlerTests
{
    private readonly Mock<IMemberAccountDeletionService> _serviceMock = new(MockBehavior.Strict);
    private readonly RequestMemberAccountDeletionCommandHandler _handler;
    public RequestMemberAccountDeletionCommandHandlerTests()
    {
        _handler = new RequestMemberAccountDeletionCommandHandler(
            _serviceMock.Object,
            NullLogger<RequestMemberAccountDeletionCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenCalled_ForwardsIdentityAndCancellation()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new RequestMemberAccountDeletionCommand(memberId);
        _serviceMock
            .Setup(service => service.RequestAsync(
                memberId,
                cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _serviceMock.Verify(
            service => service.RequestAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
