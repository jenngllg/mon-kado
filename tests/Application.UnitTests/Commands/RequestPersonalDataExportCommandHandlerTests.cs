using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class RequestPersonalDataExportCommandHandlerTests
{
    private readonly Mock<IPersonalDataExportService> _serviceMock;
    private readonly RequestPersonalDataExportCommandHandler _handler;
    public RequestPersonalDataExportCommandHandlerTests()
    {
        _serviceMock = new Mock<IPersonalDataExportService>(MockBehavior.Strict);
        _handler = new RequestPersonalDataExportCommandHandler(
            _serviceMock.Object,
            NullLogger<RequestPersonalDataExportCommandHandler>.Instance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenServiceCompletesOrFails_PreservesResultAndCancellationContract(bool fails)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var request = new RequestPersonalDataExportCommand(memberId);
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = new PersonalDataExportDetails();
        var failure = new InvalidOperationException("Expected failure");

        if (fails)
            _serviceMock
                .Setup(service => service.RequestAsync(
                    memberId,
                    cancellationToken))
                .ThrowsAsync(failure);
        else
            _serviceMock
                .Setup(service => service.RequestAsync(
                    memberId,
                    cancellationToken))
                .ReturnsAsync(result);

        // Act
        if (fails)
            Assert.Same(
                failure,
                await Assert.ThrowsAsync<InvalidOperationException>(() => _handler.Handle(
                        request,
                        cancellationToken)));
        else
            Assert.Same(
                result,
                await _handler.Handle(
                    request,
                    cancellationToken));

        // Assert
        _serviceMock.Verify(
            service => service.RequestAsync(
                memberId,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
