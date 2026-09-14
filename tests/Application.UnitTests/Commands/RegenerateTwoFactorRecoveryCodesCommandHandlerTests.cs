using AutoFixture;

using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class RegenerateTwoFactorRecoveryCodesCommandHandlerTests
{
    private readonly Mock<ITwoFactorService> _twoFactorServiceMock;
    private readonly RegenerateTwoFactorRecoveryCodesCommandHandler _handler;
    private readonly RegenerateTwoFactorRecoveryCodesCommand _request;

    public RegenerateTwoFactorRecoveryCodesCommandHandlerTests()
    {
        _twoFactorServiceMock = new Mock<ITwoFactorService>(MockBehavior.Strict);
        _handler = new RegenerateTwoFactorRecoveryCodesCommandHandler(
            _twoFactorServiceMock.Object,
            NullLogger<RegenerateTwoFactorRecoveryCodesCommandHandler>.Instance);
        _request = new RegenerateTwoFactorRecoveryCodesCommand(
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
    }

    [Fact]
    public async Task Handle_WhenServiceSucceeds_ReturnsExactResultAndForwardsCancellation()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var result = TestFixture.Create().Create<TwoFactorRecoveryCodesResponse>();
        _twoFactorServiceMock
            .Setup(service => service.RegenerateRecoveryCodesAsync(
                _request.Flow!,
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
            service => service.RegenerateRecoveryCodesAsync(
                _request.Flow!,
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
            .Setup(service => service.RegenerateRecoveryCodesAsync(
                _request.Flow!,
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
            service => service.RegenerateRecoveryCodesAsync(
                _request.Flow!,
                cancellationToken),
            Times.Once);
        _twoFactorServiceMock.VerifyNoOtherCalls();
    }
}
