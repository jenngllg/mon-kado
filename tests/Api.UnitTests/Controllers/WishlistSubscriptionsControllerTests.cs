using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Api.Controllers;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Controllers;

public class WishlistSubscriptionsControllerTests
{
    private readonly Mock<ISender> _senderMock = new(MockBehavior.Strict);
    private readonly Mock<IAuthorizationService> _authorizationMock = new(MockBehavior.Strict);
    private readonly Mock<IWishlistShareLinkUrlService> _urlsMock = new(MockBehavior.Strict);
    private readonly Mock<IWishlistShareTokenService> _tokensMock = new(MockBehavior.Strict);
    private readonly WishlistSubscriptionsController _controller;

    public WishlistSubscriptionsControllerTests()
    {
        _controller = new WishlistSubscriptionsController(
            _senderMock.Object,
            _authorizationMock.Object,
            _urlsMock.Object,
            _tokensMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task GetPageAsync_WhenSubjectIsInvalid_RejectsBeforeDispatch(string subject)
    {
        // Arrange
        _controller.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(
            JwtRegisteredClaimNames.Sub,
            subject)]));

        // Act
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(() => _controller.GetPageAsync(
            null,
            null,
            TestContext.Current.CancellationToken));

        // Assert
        VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteAsync_WhenAuthorizationFails_DoesNotDispatchMutation()
    {
        // Arrange
        var id = Guid.CreateVersion7();
        _authorizationMock.Setup(authorization => authorization.AuthorizeAsync(
                _controller.User,
                id,
                AuthorizationPolicies.ManageWishlistSubscription))
            .ReturnsAsync(AuthorizationResult.Failed());

        // Act
        await Assert.ThrowsAsync<WishlistSubscriptionNotFoundException>(() => _controller.DeleteAsync(
            id,
            TestContext.Current.CancellationToken));

        // Assert
        _authorizationMock.Verify(authorization => authorization.AuthorizeAsync(
                _controller.User,
                id,
                AuthorizationPolicies.ManageWishlistSubscription),
            Times.Once);
        VerifyNoOtherCalls();
    }

    private void VerifyNoOtherCalls()
    {
        _senderMock.VerifyNoOtherCalls();
        _authorizationMock.VerifyNoOtherCalls();
        _urlsMock.VerifyNoOtherCalls();
        _tokensMock.VerifyNoOtherCalls();
    }
}
