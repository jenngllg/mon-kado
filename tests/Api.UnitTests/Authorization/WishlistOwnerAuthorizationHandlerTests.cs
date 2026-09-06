using JennGllg.Fr.MonKado.Back.Api.Authorization;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

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

    [Theory]
    [InlineData("active", false)]
    [InlineData("active", true)]
    [InlineData("suspended", false)]
    [InlineData("suspended", true)]
    [InlineData("missing", false)]
    [InlineData("missing", true)]
    public async Task HandleAsync_WhenOwnerRequestsWritableAccess_EnforcesModerationState(
        string state,
        bool hasHttpContext)
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = hasHttpContext ? TestContext.Current.CancellationToken : CancellationToken.None;
        _httpContextAccessor.HttpContext = hasHttpContext
            ? new DefaultHttpContext { RequestAborted = cancellationToken }
            : null;
        var handler = CreateHandler();
        var user = CreateAuthorizationContext(
            memberId.ToString(),
            wishlistId).User;
        var requirement = new WishlistOwnerRequirement { RequiresWritable = true };
        var context = new AuthorizationHandlerContext(
            [requirement],
            user,
            wishlistId);
        var wishlist = state == "missing"
            ? null
            : new WishlistDetails(
                wishlistId,
                "Wishlist",
                WishlistOccasion.Other,
                null,
                null,
                DateTime.UnixEpoch,
                null,
                1)
            {
                IsSuspended = state == "suspended"
            };
        _wishlistServiceMock
            .Setup(service => service.GetAccessAsync(
                memberId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.Owner);
        _wishlistServiceMock
            .Setup(service => service.GetAsync(
                wishlistId,
                cancellationToken))
            .ReturnsAsync(wishlist);

        // Act
        var exception = await Record.ExceptionAsync(() => handler.HandleAsync(context));

        // Assert
        if (state == "suspended")
            Assert.IsType<WishlistSuspendedException>(exception);
        else
            Assert.Null(exception);

        Assert.Equal(
            state == "active",
            context.HasSucceeded);
        _wishlistServiceMock.Verify(
            service => service.GetAccessAsync(
                memberId,
                wishlistId,
                cancellationToken),
            Times.Once);
        _wishlistServiceMock.Verify(
            service => service.GetAsync(
                wishlistId,
                cancellationToken),
            Times.Once);
        _wishlistServiceMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task HandleAsync_WhenAnotherMemberRequestsWritableAccess_DoesNotReadPrivateModerationState()
    {
        // Arrange
        var memberId = Guid.CreateVersion7();
        var wishlistId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        _httpContextAccessor.HttpContext = new DefaultHttpContext { RequestAborted = cancellationToken };
        var user = CreateAuthorizationContext(
            memberId.ToString(),
            wishlistId).User;
        var requirement = new WishlistOwnerRequirement { RequiresWritable = true };
        var context = new AuthorizationHandlerContext(
            [requirement],
            user,
            wishlistId);
        var handler = CreateHandler();
        _wishlistServiceMock
            .Setup(service => service.GetAccessAsync(
                memberId,
                wishlistId,
                cancellationToken))
            .ReturnsAsync(WishlistAccess.NotOwned);

        // Act
        await handler.HandleAsync(context);

        // Assert
        Assert.False(context.HasSucceeded);
        _wishlistServiceMock.Verify(
            service => service.GetAccessAsync(
                memberId,
                wishlistId,
                cancellationToken),
            Times.Once);
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
