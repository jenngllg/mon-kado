using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Controllers;
using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

using MediatR;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Controllers;

public class SharedWishlistsControllerTests
{
    private readonly SharedWishlistsController _controller;
    private readonly Mock<IGuestSessionCookieService> _guestSessionCookieServiceMock;
    private readonly Mock<ISender> _senderMock;

    public SharedWishlistsControllerTests()
    {
        _senderMock = new Mock<ISender>(MockBehavior.Strict);
        _guestSessionCookieServiceMock = new Mock<IGuestSessionCookieService>(MockBehavior.Strict);
        _controller = new SharedWishlistsController(
            _senderMock.Object,
            _guestSessionCookieServiceMock.Object,
            new EntityTagService())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal()
                }
            }
        };
    }

    [Fact]
    public async Task GetAsync_WhenPrincipalHasNoIdentity_UsesAnonymousGuestContext()
    {
        // Arrange
        var shareLinkId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        var wishlist = new SharedWishlistDetails(
            Guid.CreateVersion7(),
            "Owner",
            "Birthday",
            WishlistOccasion.Birthday,
            null,
            null,
            []);
        _guestSessionCookieServiceMock
            .Setup(service => service.GetValue(_controller.Request))
            .Returns((string?)null);
        _senderMock
            .Setup(sender => sender.Send(
                It.Is<GetSharedWishlistQuery>(query =>
                    query.ShareLinkId == shareLinkId &&
                    query.Secret == "secret" &&
                    query.MemberId.GetValueOrDefault() == Guid.Empty &&
                    string.IsNullOrEmpty(query.GuestToken)),
                cancellationToken))
            .ReturnsAsync(new SharedWishlistResult(
                wishlist,
                null));

        // Act
        var result = await _controller.GetAsync(
            shareLinkId,
            "secret",
            cancellationToken);

        // Assert
        var response = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<SharedWishlistResponse>(response.Value);
        Assert.Equal(
            wishlist.Id,
            body.Id);
        Assert.Null(body.CurrentParticipant);
        _guestSessionCookieServiceMock.Verify(
            service => service.GetValue(_controller.Request),
            Times.Once);
        _senderMock.Verify(
            sender => sender.Send(
                It.Is<GetSharedWishlistQuery>(query =>
                    query.MemberId.GetValueOrDefault() == Guid.Empty),
                cancellationToken),
            Times.Once);
        _guestSessionCookieServiceMock.VerifyNoOtherCalls();
        _senderMock.VerifyNoOtherCalls();
    }
}
