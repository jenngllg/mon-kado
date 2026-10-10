using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

using Moq;

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Authorization;

public class WishlistSubscriptionOwnerAuthorizationHandlerTests
{
    private readonly Mock<IWishlistSubscriptionService> _serviceMock;
    private readonly HttpContextAccessor _accessor;
    private readonly WishlistSubscriptionOwnerAuthorizationHandler _handler;
    private readonly WishlistSubscriptionOwnerRequirement _requirement = new();

    public WishlistSubscriptionOwnerAuthorizationHandlerTests()
    {
        _serviceMock = new Mock<IWishlistSubscriptionService>(MockBehavior.Strict);
        _accessor = new HttpContextAccessor();
        _handler = new WishlistSubscriptionOwnerAuthorizationHandler(
            _serviceMock.Object,
            _accessor);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task HandleAsync_WhenMemberIsValid_SucceedsOnlyForAccessibleOwnedSubscription(
        bool owned,
        bool hasRequest)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        var cancellationToken = hasRequest ? TestContext.Current.CancellationToken : CancellationToken.None;

        if (hasRequest)
            _accessor.HttpContext = new DefaultHttpContext { RequestAborted = cancellationToken };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(
                JwtRegisteredClaimNames.Sub,
                memberId.ToString())],
            "test"));
        var context = new AuthorizationHandlerContext(
            [_requirement],
            principal,
            id);
        _serviceMock.Setup(service => service.GetAsync(
                memberId,
                id,
                cancellationToken))
            .ReturnsAsync(owned ? new WishlistSubscriptionDetails() : null);

        // Act
        await _handler.HandleAsync(context);

        // Assert
        Assert.Equal(
            owned,
            context.HasSucceeded);
        _serviceMock.Verify(service => service.GetAsync(
                memberId,
                id,
                cancellationToken),
            Times.Once);
        _serviceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task HandleAsync_WhenSubjectIsInvalid_RejectsWithoutLookup(string subject)
    {
        // Arrange
        var context = new AuthorizationHandlerContext(
            [_requirement],
            new ClaimsPrincipal(new ClaimsIdentity([new Claim(
                JwtRegisteredClaimNames.Sub,
                subject)])),
            Guid.CreateVersion7());

        // Act
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(() => _handler.HandleAsync(context));

        // Assert
        _serviceMock.VerifyNoOtherCalls();
    }
}
