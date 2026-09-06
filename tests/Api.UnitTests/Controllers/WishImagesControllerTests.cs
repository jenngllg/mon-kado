using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Controllers;
using JennGllg.Fr.MonKado.Back.Api.Models;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Controllers;

public class WishImagesControllerTests
{
    private readonly Mock<IWishImageUrlService> _urlServiceMock;
    private readonly Mock<IWishImageDeliveryService> _deliveryServiceMock;
    private readonly WishImagesController _controller;

    public WishImagesControllerTests()
    {
        _urlServiceMock = new Mock<IWishImageUrlService>(MockBehavior.Strict);
        _deliveryServiceMock = new Mock<IWishImageDeliveryService>(MockBehavior.Strict);
        _controller = new WishImagesController(
            _urlServiceMock.Object,
            _deliveryServiceMock.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetAsync_WhenGrantIsValid_ReturnsHardenedWebpStream(bool isOwned)
    {
        // Arrange
        var parentId = Guid.CreateVersion7();
        var wishId = Guid.CreateVersion7();
        var token = "signed-token";
        var cancellationToken = TestContext.Current.CancellationToken;
        var grant = new WishImageGrant
        {
            OwnerId = isOwned
                ? Guid.CreateVersion7()
                : null,
            ShareLinkId = isOwned
                ? null
                : parentId,
            WishlistId = Guid.CreateVersion7(),
            WishId = wishId,
            ImageId = Guid.CreateVersion7()
        };
        var stream = new MemoryStream([1]);

        if (isOwned)
        {
            grant = new WishImageGrant
            {
                OwnerId = grant.OwnerId,
                WishlistId = parentId,
                WishId = grant.WishId,
                ImageId = grant.ImageId
            };
            _urlServiceMock
                .Setup(service => service.ValidateOwned(
                    token,
                    parentId,
                    wishId))
                .Returns(grant);
            _deliveryServiceMock
                .Setup(service => service.OpenOwnedAsync(
                    grant,
                    cancellationToken))
                .ReturnsAsync(stream);
        }
        else
        {
            _urlServiceMock
                .Setup(service => service.ValidateShared(
                    token,
                    parentId,
                    wishId))
                .Returns(grant);
            _deliveryServiceMock
                .Setup(service => service.OpenSharedAsync(
                    grant,
                    cancellationToken))
                .ReturnsAsync(stream);
        }

        // Act
        var result = isOwned
            ? await _controller.GetOwnedAsync(
                parentId,
                wishId,
                token,
                cancellationToken)
            : await _controller.GetSharedAsync(
                parentId,
                wishId,
                token,
                cancellationToken);

        // Assert
        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Same(
            stream,
            file.FileStream);
        Assert.Equal(
            "image/webp",
            file.ContentType);
        Assert.False(file.EnableRangeProcessing);
        Assert.Equal(
            "no-store",
            _controller.Response.Headers.CacheControl);
        Assert.Equal(
            "nosniff",
            _controller.Response.Headers.XContentTypeOptions);

        if (isOwned)
        {
            _urlServiceMock.Verify(
                service => service.ValidateOwned(
                    token,
                    parentId,
                    wishId),
                Times.Once);
            _deliveryServiceMock.Verify(
                service => service.OpenOwnedAsync(
                    grant,
                    cancellationToken),
                Times.Once);
        }
        else
        {
            _urlServiceMock.Verify(
                service => service.ValidateShared(
                    token,
                    parentId,
                    wishId),
                Times.Once);
            _deliveryServiceMock.Verify(
                service => service.OpenSharedAsync(
                    grant,
                    cancellationToken),
                Times.Once);
        }

        _urlServiceMock.VerifyNoOtherCalls();
        _deliveryServiceMock.VerifyNoOtherCalls();
        await stream.DisposeAsync();
    }
}
