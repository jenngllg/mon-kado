using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Controllers;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Controllers;

public class GoogleAuthenticationControllerTests
{
    private readonly Mock<ISender> _senderMock = new(MockBehavior.Strict);
    private readonly Mock<IGoogleReturnPathService> _returnPathMock = new(MockBehavior.Strict);
    private readonly Mock<IGoogleExternalAuthenticationService> _externalMock = new(MockBehavior.Strict);
    private readonly Mock<IRefreshSessionService> _sessionsMock = new(MockBehavior.Strict);
    private readonly Mock<IRefreshTokenCookieService> _cookieMock = new(MockBehavior.Strict);
    private readonly GoogleAuthenticationController _controller;

    public GoogleAuthenticationControllerTests()
    {
        _controller = new GoogleAuthenticationController(
            _senderMock.Object,
            Microsoft.Extensions.Options.Options.Create(new GoogleAuthenticationOptions { Enabled = true }),
            _returnPathMock.Object,
            _externalMock.Object,
            _sessionsMock.Object,
            _cookieMock.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        _controller.Request.Scheme = "https";
    }

    [Fact]
    public async Task CompleteAsync_WhenServiceReturnsNoTokensOrChallenge_PreservesCookiesAndRejectsResult()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _senderMock
            .Setup(sender => sender.Send(
                It.IsAny<CompleteGoogleSessionCommand>(),
                cancellationToken))
            .ReturnsAsync(new TwoFactorCompletionResult());

        // Act
        var exception = await Record.ExceptionAsync(() => _controller.CompleteAsync(
            new CompleteGoogleSessionRequest("opaque-flow"),
            cancellationToken));

        // Assert
        Assert.IsType<GoogleAuthenticationFailedException>(exception);
        _senderMock.Verify(
            sender => sender.Send(
                It.IsAny<CompleteGoogleSessionCommand>(),
                cancellationToken),
            Times.Once);
        _senderMock.VerifyNoOtherCalls();
        _returnPathMock.VerifyNoOtherCalls();
        _externalMock.VerifyNoOtherCalls();
        _sessionsMock.VerifyNoOtherCalls();
        _cookieMock.VerifyNoOtherCalls();
    }
}
