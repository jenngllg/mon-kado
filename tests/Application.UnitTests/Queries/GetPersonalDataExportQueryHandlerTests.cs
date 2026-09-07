using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetPersonalDataExportQueryHandlerTests
{
    private readonly Mock<IPersonalDataExportService> _serviceMock;
    private readonly GetPersonalDataExportQueryHandler _handler;
    public GetPersonalDataExportQueryHandlerTests()
    {
        _serviceMock = new Mock<IPersonalDataExportService>(MockBehavior.Strict);
        _handler = new GetPersonalDataExportQueryHandler(
            _serviceMock.Object,
            NullLogger<GetPersonalDataExportQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenServiceCompletesOrFails_PreservesResultAndCancellationContract(bool fails)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var exportId = Guid.CreateVersion7();
        var request = new GetPersonalDataExportQuery(
            memberId,
            exportId);
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = new PersonalDataExportDetails();
        var failure = new InvalidOperationException("Expected failure");

        if (fails)
            _serviceMock
                .Setup(service => service.GetAsync(
                    memberId,
                    exportId,
                    cancellationToken))
                .ThrowsAsync(failure);
        else
            _serviceMock
                .Setup(service => service.GetAsync(
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
                memberId,
                exportId,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
