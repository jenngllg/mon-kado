using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Controllers;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.UnitTests.TestData;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Queries;

using MediatR;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Controllers;

/// <summary>Verifies public social metadata without disclosing bearer access data.</summary>
public class WishlistSharePreviewsControllerTests
{
    private const string FrontendOrigin = "https://app.example.test";
    private readonly Mock<ISender> _senderMock;
    private readonly Mock<IWishImageDeliveryService> _deliveryMock;
    private readonly Mock<IWishlistSharePreviewImageProcessor> _processorMock;
    private readonly WishlistSharePreviewsController _controller;

    /// <summary>Creates the controller with strict side-effect dependencies.</summary>
    public WishlistSharePreviewsControllerTests()
    {
        _senderMock = new Mock<ISender>(MockBehavior.Strict);
        _deliveryMock = new Mock<IWishImageDeliveryService>(MockBehavior.Strict);
        _processorMock = new Mock<IWishlistSharePreviewImageProcessor>(MockBehavior.Strict);
        _controller = new WishlistSharePreviewsController(
            _senderMock.Object,
            _deliveryMock.Object,
            _processorMock.Object,
            Microsoft.Extensions.Options.Options.Create(new WishlistSharingOptions
            {
                FrontendOrigin = FrontendOrigin
            }))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    /// <summary>Verifies canonical metadata uses trusted configuration, independent of request input.</summary>
    /// <param name="imageCount">The number of current public images.</param>
    /// <returns>The asynchronous test.</returns>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task GetAsync_WhenImageCountVaries_ReturnsTrustedPublicGraphMetadata(int imageCount)
    {
        // Arrange
        var token = TestContext.Current.CancellationToken;
        var shareLinkId = Guid.CreateVersion7();
        var preview = WishlistSharePreviewTestData.CreateWithHtmlTitle(imageCount);
        _controller.Request.Scheme = "http";
        _controller.Request.Host = new HostString("untrusted.example.test");
        _controller.Request.QueryString = new QueryString("?token=private-query-secret");
        _controller.Request.Headers["X-MonKado-Share-Token"] = "private-header-secret";
        _controller.Request.Headers["X-Forwarded-Host"] = "forwarded.example.test";
        _senderMock
            .Setup(sender => sender.Send(
                It.Is<GetWishlistSharePreviewQuery>(query => query.ShareLinkId == shareLinkId),
                token))
            .ReturnsAsync(preview);

        // Act
        var result = await _controller.GetAsync(
            shareLinkId,
            token);

        // Assert
        var content = Assert.IsType<ContentResult>(result);
        var html = Assert.IsType<string>(content.Content);
        Assert.Equal(
            "text/html; charset=utf-8",
            content.ContentType);
        Assert.Contains(
            $"<meta property=\"og:url\" content=\"{FrontendOrigin}/shared-wishlists/{shareLinkId:D}\">",
            html);
        Assert.Contains(
            "<meta property=\"og:type\" content=\"website\">",
            html);
        Assert.Contains(
            "<meta property=\"og:title\"",
            html);
        Assert.Contains(
            "<meta property=\"og:locale\" content=\"fr_FR\">",
            html);
        Assert.Contains(
            "&lt;script&gt;",
            html);
        Assert.DoesNotContain(
            "<script>",
            html);
        Assert.DoesNotContain(
            "untrusted.example.test",
            html);
        Assert.DoesNotContain(
            "forwarded.example.test",
            html);
        Assert.DoesNotContain(
            "private-query-secret",
            html);
        Assert.DoesNotContain(
            "private-header-secret",
            html);
        Assert.DoesNotContain(
            preview.WishlistId.ToString(),
            html);
        Assert.DoesNotContain(
            "fb:app_id",
            html);
        Assert.Equal(
            "no-store",
            _controller.Response.Headers.CacheControl.ToString());

        if (imageCount > 0)
        {
            Assert.Contains(
                $"<meta property=\"og:image\" content=\"{FrontendOrigin}/share-previews/{shareLinkId:D}/image\">",
                html);
            Assert.Contains(
                "<meta property=\"og:image:width\" content=\"1200\">",
                html);
            Assert.Contains(
                "<meta property=\"og:image:height\" content=\"630\">",
                html);
            Assert.Contains(
                "<meta property=\"og:image:type\" content=\"image/jpeg\">",
                html);
        }

        if (imageCount == 0)
            Assert.DoesNotContain(
                "og:image",
                html);
        _senderMock.Verify(
            sender => sender.Send(
                It.Is<GetWishlistSharePreviewQuery>(query => query.ShareLinkId == shareLinkId),
                token),
            Times.Once);
        _senderMock.VerifyNoOtherCalls();
        _deliveryMock.VerifyNoOtherCalls();
        _processorMock.VerifyNoOtherCalls();
    }
}
