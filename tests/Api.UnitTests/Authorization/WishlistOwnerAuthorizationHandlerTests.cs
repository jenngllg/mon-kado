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

public class WishlistOwnerAuthorizationHandlerTests
{
    private readonly HttpContextAccessor _httpContextAccessor = new();
    private readonly WishlistOwnerRequirement _requirement = new();
    private readonly Mock<IWishlistService> _wishlistServiceMock;

    public WishlistOwnerAuthorizationHandlerTests()
    {
        _wishlistServiceMock = new Mock<IWishlistService>(MockBehavior.Strict);
    }

    [Fact]
    public async Task HandleAsync_WhenMemberOwnsWishlist_SucceedsRequirement()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        using var cancellationSource = new CancellationTokenSource();
        _httpContextAccessor.HttpContext = new DefaultHttpContext
        {
            RequestAborted = cancellationSource.Token
        };
        var handler = CreateHandler();
        var context = CreateAuthorizationContext(
            memberId.ToString(),
            wishlistId);
        _wishlistServiceMock
            .Setup(service => service.GetAccessAsync(
                memberId,
                wishlistId,
                cancellationSource.Token))
            .ReturnsAsync(WishlistAccess.Owner);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.True(context.HasSucceeded);
        _wishlistServiceMock.Verify(
            service => service.GetAccessAsync(
                memberId,
                wishlistId,
                cancellationSource.Token),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenWishlistIsNotOwned_DoesNotSucceedRequirement()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        _httpContextAccessor.HttpContext = null;
        var handler = CreateHandler();
        var context = CreateAuthorizationContext(
            memberId.ToString(),
            wishlistId);
        _wishlistServiceMock
            .Setup(service => service.GetAccessAsync(
                memberId,
                wishlistId,
                CancellationToken.None))
            .ReturnsAsync(WishlistAccess.NotOwned);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
        _wishlistServiceMock.Verify(
            service => service.GetAccessAsync(
                memberId,
                wishlistId,
                CancellationToken.None),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenMemberDoesNotExist_ThrowsInvalidAuthenticationSessionException()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var handler = CreateHandler();
        var context = CreateAuthorizationContext(
            memberId.ToString(),
            wishlistId);
        _wishlistServiceMock
            .Setup(service => service.GetAccessAsync(
                memberId,
                wishlistId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(WishlistAccess.MemberNotFound);

        // Act
        var action = () => handler.HandleAsync(context);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        _wishlistServiceMock.Verify(
            service => service.GetAccessAsync(
                memberId,
                wishlistId,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task HandleAsync_WhenSubjectIsInvalid_ThrowsInvalidAuthenticationSessionException(
        string? subject)
    {
        // Arrange
        var handler = CreateHandler();
        var context = CreateAuthorizationContext(
            subject,
            Guid.CreateVersion7());

        // Act
        var action = () => handler.HandleAsync(context);

        // Assert
        await Assert.ThrowsAsync<InvalidAuthenticationSessionException>(action);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }

    private WishlistOwnerAuthorizationHandler CreateHandler()
    {
        return new WishlistOwnerAuthorizationHandler(
            _wishlistServiceMock.Object,
            _httpContextAccessor);
    }

    private AuthorizationHandlerContext CreateAuthorizationContext(
        string? subject,
        Guid wishlistId)
    {
        var claims = subject is null
            ? []
            : new[]
            {
                new Claim(
                    JwtRegisteredClaimNames.Sub,
                    subject)
            };
        var user = new ClaimsPrincipal(new ClaimsIdentity(claims));

        return new AuthorizationHandlerContext(
            [_requirement],
            user,
            wishlistId);
    }
}
