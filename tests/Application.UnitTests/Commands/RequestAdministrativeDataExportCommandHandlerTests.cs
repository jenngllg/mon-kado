using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class RequestAdministrativeDataExportCommandHandlerTests
{
    private readonly Mock<IAdministrativeDataExportService> _serviceMock;
    private readonly RequestAdministrativeDataExportCommandHandler _handler;
    public RequestAdministrativeDataExportCommandHandlerTests()
    {
        _serviceMock = new Mock<IAdministrativeDataExportService>(MockBehavior.Strict);
        _handler = new RequestAdministrativeDataExportCommandHandler(
            _serviceMock.Object,
            NullLogger<RequestAdministrativeDataExportCommandHandler>.Instance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenServiceCompletesOrFails_PreservesResultAndCancellationContract(bool fails)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var administratorId = Guid.CreateVersion7();
        var request = new RequestAdministrativeDataExportCommand(
            administratorId,
            memberId,
            "  SUPPORT-807  ");
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = new PersonalDataExportDetails();
        var failure = new InvalidOperationException("Expected failure");

        if (fails)
            _serviceMock
                .Setup(service => service.RequestAsync(
                    administratorId,
                    memberId,
                    "SUPPORT-807",
                    cancellationToken))
                .ThrowsAsync(failure);
        else
            _serviceMock
                .Setup(service => service.RequestAsync(
                    administratorId,
                    memberId,
                    "SUPPORT-807",
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
                administratorId,
                memberId,
                "SUPPORT-807",
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
