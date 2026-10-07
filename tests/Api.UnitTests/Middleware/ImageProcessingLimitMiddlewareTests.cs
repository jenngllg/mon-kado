using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Middleware;
using JennGllg.Fr.MonKado.Back.Api.Services;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Middleware;

public class ImageProcessingLimitMiddlewareTests
{
    private readonly Mock<IImageProcessingLimiter> _limiterMock = new(MockBehavior.Strict);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_WhenPublicImageIsMarked_AcquiresWithoutAuthentication(bool missingIdentity)
    {
        // Arrange
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0
        });
        using var lease = limiter.AttemptAcquire();
        var context = CreatePublicImageContext();

        if (missingIdentity)
            context.User = new ClaimsPrincipal();
        _limiterMock.Setup(service => service.AcquireAsync(context.RequestAborted))
            .ReturnsAsync(lease);
        var called = false;
        var middleware = new ImageProcessingLimitMiddleware(
            _ =>
            {
                called = true;

                return Task.CompletedTask;
            },
            _limiterMock.Object);

        // Act
        await middleware.InvokeAsync(context);
        using var released = limiter.AttemptAcquire();

        // Assert
        Assert.True(called);
        Assert.True(released.IsAcquired);
        _limiterMock.Verify(
            service => service.AcquireAsync(context.RequestAborted),
            Times.Once);
        _limiterMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_WhenPublicRenderingAndUploadsCompete_BoundsSharedAdmissionAndRejectsOverflow()
    {
        // Arrange
        using var limiter = new ImageProcessingLimiter(new FrozenTimerTimeProvider());
        using var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var active = 0;
        var maximumActive = 0;
        var completed = 0;
        var middleware = new ImageProcessingLimitMiddleware(
            async context =>
            {
                var current = Interlocked.Increment(ref active);
                maximumActive = Math.Max(
                    maximumActive,
                    current);
                started.TrySetResult();
                await release.Task.WaitAsync(context.RequestAborted);
                Interlocked.Decrement(ref active);
                Interlocked.Increment(ref completed);
            },
            limiter);
        var publicContext = CreatePublicImageContext();
        var uploadContext = CreateContext(
            AuthenticationRateLimitingExtensions.GiftImageUploadPolicy,
            true,
            true);
        var queuedPublicContext = CreatePublicImageContext();
        var overflowContext = CreatePublicImageContext();
        overflowContext.RequestServices = services;
        using var body = new MemoryStream();
        overflowContext.Response.Body = body;

        // Act
        var first = middleware.InvokeAsync(publicContext);
        await started.Task.WaitAsync(TestContext.Current.CancellationToken);
        var upload = middleware.InvokeAsync(uploadContext);
        var queuedPublic = middleware.InvokeAsync(queuedPublicContext);
        await middleware.InvokeAsync(overflowContext);
        var overflowStatus = overflowContext.Response.StatusCode;
        release.SetResult();
        await Task.WhenAll(
            first,
            upload,
            queuedPublic);

        // Assert
        Assert.Equal(
            StatusCodes.Status429TooManyRequests,
            overflowStatus);
        Assert.Equal(
            1,
            maximumActive);
        Assert.Equal(
            3,
            completed);
        _limiterMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_WhenPrincipalHasNoIdentity_DoesNotAcquire()
    {
        // Arrange
        var context = CreateContext(
            AuthenticationRateLimitingExtensions.GiftImageUploadPolicy,
            true,
            false);
        context.User = new ClaimsPrincipal();
        var called = false;
        var middleware = new ImageProcessingLimitMiddleware(
            _ =>
            {
                called = true;

                return Task.CompletedTask;
            },
            _limiterMock.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(called);
        _limiterMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, false)]
    [InlineData("other", true, true)]
    [InlineData(AuthenticationRateLimitingExtensions.GiftImageUploadPolicy, true, false)]
    [InlineData(AuthenticationRateLimitingExtensions.ProfileImageUploadPolicy, true, false)]
    public async Task InvokeAsync_WhenNotAuthenticatedUpload_DoesNotAcquire(
        string? policy,
        bool endpoint,
        bool authenticated)
    {
        // Arrange
        var context = CreateContext(
            policy,
            endpoint,
            authenticated);
        var called = false;
        var middleware = new ImageProcessingLimitMiddleware(
            _ =>
            {
                called = true;

                return Task.CompletedTask;
            },
            _limiterMock.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(called);
        _limiterMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(AuthenticationRateLimitingExtensions.GiftImageUploadPolicy, false)]
    [InlineData(AuthenticationRateLimitingExtensions.ProfileImageUploadPolicy, true)]
    [InlineData(AuthenticationRateLimitingExtensions.WishImportPreviewPolicy, false)]
    public async Task InvokeAsync_WhenAdmitted_ReleasesPermitEvenAfterFailure(
        string policy,
        bool fail)
    {
        // Arrange
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0
        });
        using var lease = limiter.AttemptAcquire();
        var context = CreateContext(
            policy,
            true,
            true);
        _limiterMock.Setup(service => service.AcquireAsync(context.RequestAborted))
            .ReturnsAsync(lease);
        var middleware = new ImageProcessingLimitMiddleware(
            _ =>
            {
                using var occupied = limiter.AttemptAcquire();
                Assert.False(occupied.IsAcquired);

                if (fail)
                    throw new InvalidOperationException("test-only");

                return Task.CompletedTask;
            },
            _limiterMock.Object);

        // Act
        var exception = await Record.ExceptionAsync(() => middleware.InvokeAsync(context));
        using var released = limiter.AttemptAcquire();

        // Assert
        Assert.Equal(
            fail,
            exception is InvalidOperationException);
        Assert.True(released.IsAcquired);
        _limiterMock.Verify(
            service => service.AcquireAsync(context.RequestAborted),
            Times.Once);
        _limiterMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvokeAsync_WhenAdmissionFails_ReturnsStructuredRejectionWithoutReadingBody(bool timedOut)
    {
        // Arrange
        using var limiter = new ConcurrencyLimiter(new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0
        });
        using var occupied = limiter.AttemptAcquire();
        using var rejection = timedOut ? null : limiter.AttemptAcquire();
        using var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        var context = CreateContext(
            AuthenticationRateLimitingExtensions.GiftImageUploadPolicy,
            true,
            true);
        context.RequestServices = services;
        context.Response.Body = new MemoryStream();
        context.Request.Body = new MemoryStream([
            1,
            2,
            3
        ]);
        _limiterMock.Setup(service => service.AcquireAsync(context.RequestAborted))
            .ReturnsAsync(rejection);
        var middleware = new ImageProcessingLimitMiddleware(
            _ => throw new InvalidOperationException("Must not bind the upload."),
            _limiterMock.Object);

        // Act
        await middleware.InvokeAsync(context);
        context.Response.Body.Position = 0;
        using var body = await JsonDocument.ParseAsync(
            context.Response.Body,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            429,
            context.Response.StatusCode);
        Assert.Equal(
            "60",
            context.Response.Headers.RetryAfter);
        Assert.Equal(
            "no-store",
            context.Response.Headers.CacheControl);
        Assert.Equal(
            "REQUEST_RATE_LIMIT_EXCEEDED",
            body.RootElement.GetProperty("errorCode")
                .GetString());
        Assert.Equal(
            0,
            context.Request.Body.Position);
        _limiterMock.Verify(
            service => service.AcquireAsync(context.RequestAborted),
            Times.Once);
        _limiterMock.VerifyNoOtherCalls();
    }

    private static DefaultHttpContext CreateContext(
        string? policy,
        bool endpoint,
        bool authenticated)
    {
        var context = new DefaultHttpContext { RequestAborted = TestContext.Current.CancellationToken };

        if (endpoint)
            context.SetEndpoint(new Endpoint(
                null,
                new EndpointMetadataCollection(policy is null ? [] : [new EnableRateLimitingAttribute(policy)]),
                "test"));

        if (authenticated)
            context.User = new ClaimsPrincipal(new ClaimsIdentity("Bearer"));

        return context;
    }

    private static DefaultHttpContext CreatePublicImageContext()
    {
        var context = CreateContext(
            AuthenticationRateLimitingExtensions.SharedWishlistPolicy,
            true,
            false);
        context.SetEndpoint(new Endpoint(
            null,
            new EndpointMetadataCollection(
                new EnableRateLimitingAttribute(AuthenticationRateLimitingExtensions.SharedWishlistPolicy),
                new PublicImageProcessingAttribute()),
            "public-image"));

        return context;
    }
}
