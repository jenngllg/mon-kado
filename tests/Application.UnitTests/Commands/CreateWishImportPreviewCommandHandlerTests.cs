using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CreateWishImportPreviewCommandHandlerTests
{
    private readonly Mock<IWishImportService> _serviceMock;
    private readonly CreateWishImportPreviewCommandHandler _handler;
    public CreateWishImportPreviewCommandHandlerTests()
    {
        _serviceMock = new(MockBehavior.Strict);
        _handler = new(
            _serviceMock.Object,
            NullLogger<CreateWishImportPreviewCommandHandler>.Instance);
    }

    [Fact]
    public async Task Handle_WhenRequestIsValid_ForwardsOriginalCancellationToken()
    {
        // Arrange
        var command = WishImportTestData.CreateValidCommand();
        var preview = new WishImportPreview
        {
            Url = command.Url,
            Quantity = 1
        };
        var token = TestContext.Current.CancellationToken;
        _serviceMock
            .Setup(service => service.PreviewAsync(
                "https://example.com",
                token))
            .ReturnsAsync(preview);

        // Act
        var result = await _handler.Handle(
            command,
            token);

        // Assert
        Assert.Same(
            preview,
            result);
        _serviceMock.Verify(
            service => service.PreviewAsync(
                "https://example.com",
                token),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }
}
