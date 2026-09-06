using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class ConfirmMemberAccountDeletionCommandHandlerTests
{
    private readonly Mock<IMemberAccountDeletionService> _serviceMock = new(MockBehavior.Strict);
    private readonly ConfirmMemberAccountDeletionCommandHandler _handler;
    public ConfirmMemberAccountDeletionCommandHandlerTests()
    {
        _handler = new ConfirmMemberAccountDeletionCommandHandler(
            _serviceMock.Object,
            NullLogger<ConfirmMemberAccountDeletionCommandHandler>.Instance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("protected-token")]
    public async Task Handle_WhenCalled_ForwardsIdentityAndCancellation(string? token)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var command = new ConfirmMemberAccountDeletionCommand(
            memberId,
            token);
        _serviceMock
            .Setup(service => service.ConfirmAsync(
                memberId,
                token ?? string.Empty,
                cancellationToken))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(
            command,
            cancellationToken);

        // Assert
        _serviceMock.Verify(
            service => service.ConfirmAsync(
                memberId,
                token ?? string.Empty,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
