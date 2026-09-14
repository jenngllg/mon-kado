using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CompleteTwoFactorCommandHandlerTests
{
    private readonly Mock<ITwoFactorService> _twoFactorServiceMock;
    private readonly CompleteTwoFactorCommandHandler _handler;
    private readonly CompleteTwoFactorCommand _request;

    public CompleteTwoFactorCommandHandlerTests()
    {
        _twoFactorServiceMock = new Mock<ITwoFactorService>(MockBehavior.Strict);
        _handler = new CompleteTwoFactorCommandHandler(
            _twoFactorServiceMock.Object,
            NullLogger<CompleteTwoFactorCommandHandler>.Instance);
        _request = new CompleteTwoFactorCommand(
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "123456",
            null);
    }

    [Fact]
    public async Task Handle_WhenServiceSucceeds_ReturnsExactResultAndForwardsCancellation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = TestFixture.Create().Create<TwoFactorCompletionResult>();
        _twoFactorServiceMock
            .Setup(service => service.CompleteAsync(
                _request.Flow!,
                _request.Code,
                _request.RecoveryCode,
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
            service => service.CompleteAsync(
                _request.Flow!,
                _request.Code,
                _request.RecoveryCode,
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
            .Setup(service => service.CompleteAsync(
                _request.Flow!,
                _request.Code,
                _request.RecoveryCode,
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
            service => service.CompleteAsync(
                _request.Flow!,
                _request.Code,
                _request.RecoveryCode,
                cancellationToken),
            Times.Once);
        _twoFactorServiceMock.VerifyNoOtherCalls();
    }
}
