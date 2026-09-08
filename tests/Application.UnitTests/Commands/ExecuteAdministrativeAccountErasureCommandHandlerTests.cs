using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class ExecuteAdministrativeAccountErasureCommandHandlerTests
{
    private readonly Mock<IAdministrativeAccountErasureService> _serviceMock;
    private readonly ExecuteAdministrativeAccountErasureCommandHandler _handler;

    public ExecuteAdministrativeAccountErasureCommandHandlerTests()
    {
        _serviceMock = new Mock<IAdministrativeAccountErasureService>(MockBehavior.Strict);
        _handler = new ExecuteAdministrativeAccountErasureCommandHandler(
            _serviceMock.Object,
            NullLogger<ExecuteAdministrativeAccountErasureCommandHandler>.Instance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenServiceCompletesOrFails_ForwardsNormalizedReferenceAndCancellation(bool fails)
    {
        // Arrange
        var administratorId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var request = new ExecuteAdministrativeAccountErasureCommand(
            administratorId,
            memberId,
            memberId,
            "  SUPPORT-808  ");
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new InvalidOperationException("Expected service failure");
        _serviceMock.Setup(service => service.ExecuteAsync(
                administratorId,
                memberId,
                "SUPPORT-808",
                cancellationToken))
            .Returns(() => fails ? Task.FromException<Guid>(failure) : Task.FromResult(Guid.CreateVersion7()));

        // Act
        var exception = await Record.ExceptionAsync(() => _handler.Handle(
            request,
            cancellationToken));

        // Assert
        Assert.Same(
            fails ? failure : null,
            exception);
        _serviceMock.Verify(service => service.ExecuteAsync(
                administratorId,
                memberId,
                "SUPPORT-808",
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
