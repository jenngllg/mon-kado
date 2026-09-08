using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetAdministrativeDataExportQueryHandlerTests
{
    private readonly Mock<IAdministrativeDataExportService> _serviceMock;
    private readonly GetAdministrativeDataExportQueryHandler _handler;
    public GetAdministrativeDataExportQueryHandlerTests()
    {
        _serviceMock = new Mock<IAdministrativeDataExportService>(MockBehavior.Strict);
        _handler = new GetAdministrativeDataExportQueryHandler(
            _serviceMock.Object,
            NullLogger<GetAdministrativeDataExportQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenServiceCompletesOrFails_PreservesResultAndCancellationContract(bool fails)
    {
        // Arrange
        var administratorId = Guid.CreateVersion7();
        var memberId = Guid.CreateVersion7();
        var exportId = Guid.CreateVersion7();
        var request = new GetAdministrativeDataExportQuery(
            administratorId,
            memberId,
            exportId);
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = new PersonalDataExportDetails();
        var failure = new InvalidOperationException("Expected failure");

        if (fails)
            _serviceMock
                .Setup(service => service.GetAsync(
                    administratorId,
                    memberId,
                    exportId,
                    cancellationToken))
                .ThrowsAsync(failure);
        else
            _serviceMock
                .Setup(service => service.GetAsync(
                    administratorId,
                    memberId,
                    exportId,
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
            service => service.GetAsync(
                administratorId,
                memberId,
                exportId,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
