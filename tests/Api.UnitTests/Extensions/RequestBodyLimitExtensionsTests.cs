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
