using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class DownloadPersonalDataExportQueryHandlerTests
{
    private readonly Mock<IPersonalDataExportService> _serviceMock;
    private readonly DownloadPersonalDataExportQueryHandler _handler;
    public DownloadPersonalDataExportQueryHandlerTests()
    {
        _serviceMock = new Mock<IPersonalDataExportService>(MockBehavior.Strict);
        _handler = new DownloadPersonalDataExportQueryHandler(
            _serviceMock.Object,
            NullLogger<DownloadPersonalDataExportQueryHandler>.Instance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handle_WhenServiceCompletesOrFails_PreservesResultAndCancellationContract(bool fails)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var exportId = Guid.CreateVersion7();
        var request = new DownloadPersonalDataExportQuery(
            memberId,
            exportId);
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = new PersonalDataExportDownload();
        var failure = new InvalidOperationException("Expected failure");

        if (fails)
            _serviceMock
                .Setup(service => service.OpenArchiveAsync(
                    memberId,
                    exportId,
                    cancellationToken))
                .ThrowsAsync(failure);
        else
            _serviceMock
                .Setup(service => service.OpenArchiveAsync(
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
            service => service.OpenArchiveAsync(
                memberId,
                exportId,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
