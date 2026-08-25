using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Extensions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using System.Net.Http.Json;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Extensions;

public class RequestBodyLimitExtensionsTests
{
    private const long MaximumRequestBodySize = 4 * 1024;

    [Fact]
    public async Task UseRequestBodyLimits_WhenBodySizeFeatureIsWritable_ConfiguresMaximumSize()
    {
        // Arrange
        var featureMock = new Mock<IHttpMaxRequestBodySizeFeature>(MockBehavior.Strict);
        featureMock
            .SetupGet(feature => feature.IsReadOnly)
            .Returns(false);
        featureMock
            .SetupSet(feature => feature.MaxRequestBodySize = MaximumRequestBodySize);
        var context = CreateContext();
        context.Request.ContentLength = 0;
        context.Features.Set(featureMock.Object);
        var nextCalled = false;
        var application = new ApplicationBuilder(context.RequestServices);
        application.UseRequestBodyLimits();
        application.Run(_ =>
        {
            nextCalled = true;

            return Task.CompletedTask;
        });
        var pipeline = application.Build();

        // Act
        await pipeline(context);

        // Assert
        Assert.True(nextCalled);
        featureMock.VerifyGet(
            feature => feature.IsReadOnly,
            Times.Once);
        featureMock.VerifySet(
            feature => feature.MaxRequestBodySize = MaximumRequestBodySize,
            Times.Once);
        featureMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UseRequestBodyLimits_WhenServerRejectsUnknownLengthBody_ReturnsPayloadTooLarge()
    {
        // Arrange
        var context = CreateContext();
        context.Request.Body = new PayloadTooLargeReadStream();
        var nextCalled = false;
        var application = new ApplicationBuilder(context.RequestServices);
        application.UseRequestBodyLimits();
        application.Run(_ =>
        {
            nextCalled = true;

            return Task.CompletedTask;
        });
        var pipeline = application.Build();

        // Act
        await pipeline(context);

        // Assert
        context.Response.Body.Position = 0;
        using var responseContent = new StreamContent(context.Response.Body);
        var response = await responseContent
            .ReadFromJsonAsync<ErrorResponse>(TestContext.Current.CancellationToken);
        Assert.False(nextCalled);
        Assert.Equal(
            StatusCodes.Status413PayloadTooLarge,
            context.Response.StatusCode);
        Assert.Equal(
            "no-store",
            context.Response.Headers.CacheControl);
        Assert.NotNull(response);
        Assert.Equal(
            ErrorCodes.RequestPayloadTooLarge,
            response.ErrorCode);
    }

    [Theory]
    [InlineData("/api/v1/wishlists/0198eaa7-1d33-7769-a9f8-9df63504b6f1")]
    [InlineData("/api/v1/wishlists/0198eaa7-1d33-7769-a9f8-9df63504b6f1/")]
    public async Task UseRequestBodyLimits_WhenPutTargetsWishlistResource_ConfiguresMaximumSize(
        string requestPath)
    {
        // Arrange
        var featureMock = new Mock<IHttpMaxRequestBodySizeFeature>(MockBehavior.Strict);
        featureMock
            .SetupGet(feature => feature.IsReadOnly)
            .Returns(false);
        featureMock
            .SetupSet(feature => feature.MaxRequestBodySize = MaximumRequestBodySize);
        var context = CreateContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = requestPath;
        context.Request.ContentLength = 0;
        context.Features.Set(featureMock.Object);
        var nextCalled = false;
        var application = new ApplicationBuilder(context.RequestServices);
        application.UseRequestBodyLimits();
        application.Run(_ =>
        {
            nextCalled = true;

            return Task.CompletedTask;
        });
        var pipeline = application.Build();

        // Act
        await pipeline(context);

        // Assert
        Assert.True(nextCalled);
        featureMock.VerifyGet(
            feature => feature.IsReadOnly,
            Times.Once);
        featureMock.VerifySet(
            feature => feature.MaxRequestBodySize = MaximumRequestBodySize,
            Times.Once);
        featureMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("/api/v1/wishlists")]
    [InlineData("/api/v1/wishlists/")]
    [InlineData("/api/v1/wishlists/not-a-guid")]
    [InlineData("/api/v1/wishlists/0198eaa7-1d33-7769-a9f8-9df63504b6f1/wishes")]
    [InlineData("/api/v1/wishlists/0198eaa7-1d33-7769-a9f8-9df63504b6f1//")]
    [InlineData("/api/v1/other/0198eaa7-1d33-7769-a9f8-9df63504b6f1")]
    public async Task UseRequestBodyLimits_WhenPutDoesNotTargetWishlistResource_DoesNotConfigureMaximumSize(
        string requestPath)
    {
        // Arrange
        var context = CreateContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = requestPath;
        context.Request.ContentLength = 0;
        var nextCalled = false;
        var application = new ApplicationBuilder(context.RequestServices);
        application.UseRequestBodyLimits();
        application.Run(_ =>
        {
            nextCalled = true;

            return Task.CompletedTask;
        });
        var pipeline = application.Build();

        // Act
        await pipeline(context);

        // Assert
        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/api/v1/wishlists/0198eaa7-1d33-7769-a9f8-9df63504b6f1/wishes/0198eaa7-1d33-7769-a9f8-9df63504b6f2")]
    [InlineData("/api/v1/wishlists/0198eaa7-1d33-7769-a9f8-9df63504b6f1/wishes/0198eaa7-1d33-7769-a9f8-9df63504b6f2/")]
    [InlineData("/API/V1/WISHLISTS/0198eaa7-1d33-7769-a9f8-9df63504b6f1/WISHES/0198eaa7-1d33-7769-a9f8-9df63504b6f2")]
    public async Task UseRequestBodyLimits_WhenPutTargetsWishResource_ConfiguresMaximumSize(
        string requestPath)
    {
        // Arrange
        var featureMock = new Mock<IHttpMaxRequestBodySizeFeature>(MockBehavior.Strict);
        featureMock
            .SetupGet(feature => feature.IsReadOnly)
            .Returns(false);
        featureMock
            .SetupSet(feature => feature.MaxRequestBodySize = MaximumRequestBodySize);
        var context = CreateContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = requestPath;
        context.Request.ContentLength = 0;
        context.Features.Set(featureMock.Object);
        var nextCalled = false;
        var application = new ApplicationBuilder(context.RequestServices);
        application.UseRequestBodyLimits();
        application.Run(_ =>
        {
            nextCalled = true;

            return Task.CompletedTask;
        });
        var pipeline = application.Build();

        // Act
        await pipeline(context);

        // Assert
        Assert.True(nextCalled);
        featureMock.VerifyGet(
            feature => feature.IsReadOnly,
            Times.Once);
        featureMock.VerifySet(
            feature => feature.MaxRequestBodySize = MaximumRequestBodySize,
            Times.Once);
        featureMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("/api/v1/wishlists/not-a-guid/wishes/0198eaa7-1d33-7769-a9f8-9df63504b6f2")]
    [InlineData("/api/v1/wishlists/0198eaa7-1d33-7769-a9f8-9df63504b6f1/wishes/not-a-guid")]
    [InlineData("/api/v1/wishlists/0198eaa7-1d33-7769-a9f8-9df63504b6f1/wishes/0198eaa7-1d33-7769-a9f8-9df63504b6f2/extra")]
    [InlineData("/api/v1/wishlists/0198eaa7-1d33-7769-a9f8-9df63504b6f1/gifts/0198eaa7-1d33-7769-a9f8-9df63504b6f2")]
    public async Task UseRequestBodyLimits_WhenPutDoesNotTargetWishResource_DoesNotConfigureMaximumSize(
        string requestPath)
    {
        // Arrange
        var context = CreateContext();
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = requestPath;
        context.Request.ContentLength = 0;
        var nextCalled = false;
        var application = new ApplicationBuilder(context.RequestServices);
        application.UseRequestBodyLimits();
        application.Run(_ =>
        {
            nextCalled = true;

            return Task.CompletedTask;
        });
        var pipeline = application.Build();

        // Act
        await pipeline(context);

        // Assert
        Assert.True(nextCalled);
    }

    [Theory]
    [InlineData("/api/v1/wishlists/0198eaa7-1d33-7769-a9f8-9df63504b6f1/wishes")]
    [InlineData("/api/v1/wishlists/0198eaa7-1d33-7769-a9f8-9df63504b6f1/wishes/")]
    [InlineData("/API/V1/WISHLISTS/0198eaa7-1d33-7769-a9f8-9df63504b6f1/WISHES")]
    public async Task UseRequestBodyLimits_WhenPostTargetsWishCollection_ConfiguresMaximumSize(
        string requestPath)
    {
        // Arrange
        var featureMock = new Mock<IHttpMaxRequestBodySizeFeature>(MockBehavior.Strict);
        featureMock
            .SetupGet(feature => feature.IsReadOnly)
            .Returns(false);
        featureMock
            .SetupSet(feature => feature.MaxRequestBodySize = MaximumRequestBodySize);
        var context = CreateContext();
        context.Request.Path = requestPath;
        context.Request.ContentLength = 0;
        context.Features.Set(featureMock.Object);
        var nextCalled = false;
        var application = new ApplicationBuilder(context.RequestServices);
        application.UseRequestBodyLimits();
        application.Run(_ =>
        {
            nextCalled = true;

            return Task.CompletedTask;
        });
        var pipeline = application.Build();

        // Act
        await pipeline(context);

        // Assert
        Assert.True(nextCalled);
        featureMock.VerifyGet(
            feature => feature.IsReadOnly,
            Times.Once);
        featureMock.VerifySet(
            feature => feature.MaxRequestBodySize = MaximumRequestBodySize,
            Times.Once);
        featureMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("/api/v1/wishlists/not-a-guid/wishes")]
    [InlineData("/api/v1/wishlists/0198eaa7-1d33-7769-a9f8-9df63504b6f1/wishes/extra")]
    [InlineData("/api/v1/wishlists//wishes")]
    [InlineData("/api/v1/wishlists/0198eaa7-1d33-7769-a9f8-9df63504b6f1/gifts")]
    public async Task UseRequestBodyLimits_WhenPostDoesNotTargetWishCollection_DoesNotConfigureMaximumSize(
        string requestPath)
    {
        // Arrange
        var context = CreateContext();
        context.Request.Path = requestPath;
        context.Request.ContentLength = 0;
        var nextCalled = false;
        var application = new ApplicationBuilder(context.RequestServices);
        application.UseRequestBodyLimits();
        application.Run(_ =>
        {
            nextCalled = true;

            return Task.CompletedTask;
        });
        var pipeline = application.Build();

        // Act
        await pipeline(context);

        // Assert
        Assert.True(nextCalled);
    }

    private static DefaultHttpContext CreateContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var context = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider()
        };
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/api/v1/auth/google/callback";
        context.RequestAborted = TestContext.Current.CancellationToken;
        context.Response.Body = new MemoryStream();

        return context;
    }
}
