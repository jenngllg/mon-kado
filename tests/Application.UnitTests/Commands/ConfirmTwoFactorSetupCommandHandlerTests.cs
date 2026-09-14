using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class ConfirmTwoFactorSetupCommandHandlerTests
{
    private readonly Mock<ITwoFactorService> _twoFactorServiceMock;
    private readonly ConfirmTwoFactorSetupCommandHandler _handler;
    private readonly ConfirmTwoFactorSetupCommand _request;

    public ConfirmTwoFactorSetupCommandHandlerTests()
    {
        _twoFactorServiceMock = new Mock<ITwoFactorService>(MockBehavior.Strict);
        _handler = new ConfirmTwoFactorSetupCommandHandler(
            _twoFactorServiceMock.Object,
            NullLogger<ConfirmTwoFactorSetupCommandHandler>.Instance);
        _request = new ConfirmTwoFactorSetupCommand(
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "123456");
    }

    [Fact]
    public async Task Handle_WhenServiceSucceeds_ReturnsExactResultAndForwardsCancellation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = TestFixture.Create().Create<TwoFactorRecoveryCodesResponse>();
        _twoFactorServiceMock
            .Setup(service => service.ConfirmSetupAsync(
                _request.Flow!,
                _request.Code!,
                cancellationToken))
            .ReturnsAsync(result);

        // Act
        var actual = await _handler.Handle(
            _request,
            cancellationToken);

        // Assert
        Assert.Same(
            result,
            actual);
        _twoFactorServiceMock.Verify(
            service => service.ConfirmSetupAsync(
                _request.Flow!,
                _request.Code!,
                cancellationToken),
            Times.Once);
        _twoFactorServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Handle_WhenServiceFails_DoesNotRetryOrTransformFailure()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new InvalidOperationException("The operation could not be confirmed.");
        _twoFactorServiceMock
            .Setup(service => service.ConfirmSetupAsync(
                _request.Flow!,
                _request.Code!,
                cancellationToken))
            .ThrowsAsync(failure);

        // Act
        var actual = await Record.ExceptionAsync(() => _handler.Handle(
            _request,
            cancellationToken));

        // Assert
        Assert.Same(
            failure,
            actual);
        _twoFactorServiceMock.Verify(
            service => service.ConfirmSetupAsync(
                _request.Flow!,
                _request.Code!,
                cancellationToken),
            Times.Once);
        _twoFactorServiceMock.VerifyNoOtherCalls();
    }
}
