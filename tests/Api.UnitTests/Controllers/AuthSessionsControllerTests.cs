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

public class AuthSessionsControllerTests
{
    private readonly Mock<ISender> _senderMock = new(MockBehavior.Strict);
    private readonly Mock<IRefreshTokenCookieService> _cookieMock = new(MockBehavior.Strict);
    private readonly Mock<IEntityTagService> _entityTagMock = new(MockBehavior.Strict);
    private readonly Mock<IProfileImageUrlService> _imageUrlMock = new(MockBehavior.Strict);
    private readonly AuthSessionsController _controller;

    public AuthSessionsControllerTests()
    {
        _controller = new AuthSessionsController(
            _senderMock.Object,
            _cookieMock.Object,
            _entityTagMock.Object,
            _imageUrlMock.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Fact]
    public async Task LoginAsync_WhenServiceReturnsNoTokensOrChallenge_DoesNotWriteCookie()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        _cookieMock
            .Setup(cookie => cookie.GetValue(_controller.Request))
            .Returns((string?)null);
        _senderMock
            .Setup(sender => sender.Send(
                It.IsAny<LoginCommand>(),
                cancellationToken))
            .ReturnsAsync(new AccountSessionLoginResult(
                AccountLoginResult.Success,
                null));

        // Act
        var exception = await Record.ExceptionAsync(() => _controller.LoginAsync(
            new LoginRequest(
                "member@example.test",
                "test-password",
                false),
            cancellationToken));

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
        _cookieMock.Verify(
            cookie => cookie.GetValue(_controller.Request),
            Times.Once);
        _senderMock.Verify(
            sender => sender.Send(
                It.IsAny<LoginCommand>(),
                cancellationToken),
            Times.Once);
        _cookieMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
        _entityTagMock.VerifyNoOtherCalls();
        _imageUrlMock.VerifyNoOtherCalls();
    }
}
