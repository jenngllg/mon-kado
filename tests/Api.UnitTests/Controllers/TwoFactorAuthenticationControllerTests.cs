using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;
using JennGllg.Fr.MonKado.Back.Api.Controllers;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Controllers;

public class TwoFactorAuthenticationControllerTests
{
    private readonly Mock<ISender> _senderMock = new(MockBehavior.Strict);
    private readonly Mock<IRefreshTokenCookieService> _cookieServiceMock = new(MockBehavior.Strict);
    private readonly TwoFactorAuthenticationController _controller;

    public TwoFactorAuthenticationControllerTests()
    {
        _controller = new TwoFactorAuthenticationController(
            _senderMock.Object,
            _cookieServiceMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Fact]
    public async Task CompleteAsync_WhenServiceReturnsNeitherChallengeNorTokens_DoesNotWriteRefreshCookie()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var request = new TwoFactorCompletionRequest { Flow = "opaque-flow" };
        _senderMock
            .Setup(sender => sender.Send(
                It.Is<CompleteTwoFactorCommand>(command => command.Flow == request.Flow),
                cancellationToken))
            .ReturnsAsync(new TwoFactorCompletionResult());

        // Act
        var exception = await Record.ExceptionAsync(() => _controller.CompleteAsync(
            request,
            cancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal(
            "no-store",
            _controller.Response.Headers.CacheControl.ToString());
        _senderMock.Verify(
            sender => sender.Send(
                It.Is<CompleteTwoFactorCommand>(command => command.Flow == request.Flow),
                cancellationToken),
            Times.Once);
        _senderMock.VerifyNoOtherCalls();
        _cookieServiceMock.VerifyNoOtherCalls();
    }
}
