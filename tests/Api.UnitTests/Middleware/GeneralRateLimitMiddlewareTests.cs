using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Api.Middleware;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using System.Threading.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Middleware;

public class GeneralRateLimitMiddlewareTests
{
    private readonly Mock<IGeneralRequestLimiter> _limiterMock = new(MockBehavior.Strict);

    [Theory]
    [InlineData("OPTIONS", "/api/v1/wishlists")]
    [InlineData("GET", "/liveness")]
    [InlineData("GET", "/readiness")]
    [InlineData("GET", "/openapi/v1.json")]
    [InlineData("GET", "/api/v10/wishlists")]
    public async Task InvokeAsync_WhenExempt_DoesNotAcquireQuota(
        string method,
        string path)
    {
        // Arrange
        var called = false;
        var middleware = new GeneralRateLimitMiddleware(
            _ =>
            {
                called = true;

                return Task.CompletedTask;
            },
            _limiterMock.Object);
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(called);
        _limiterMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("/api/v1/wishlists", true)]
    [InlineData("/api/v1/wishlists", false)]
    [InlineData("/security/csrf-token", false)]
    [InlineData("/security/csrf-token/", false)]
    [InlineData("/SECURITY/CSRF-TOKEN", false)]
    public async Task InvokeAsync_WhenLimited_RejectsBeforeNextMiddleware(
        string path,
        bool admitted)
    {
        // Arrange
        var called = false;
        using var limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });

        if (!admitted)
            limiter.AttemptAcquire().Dispose();

        using var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        _limiterMock.Setup(service => service.AttemptAcquire(context))
            .Returns(limiter.AttemptAcquire());
        var middleware = new GeneralRateLimitMiddleware(
            _ =>
            {
                called = true;

                return Task.CompletedTask;
            },
            _limiterMock.Object);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(
            admitted,
            called);
        Assert.Equal(
            admitted ? 200 : 429,
            context.Response.StatusCode);
        _limiterMock.Verify(
            service => service.AttemptAcquire(context),
            Times.Once);
        _limiterMock.VerifyNoOtherCalls();
    }
}
