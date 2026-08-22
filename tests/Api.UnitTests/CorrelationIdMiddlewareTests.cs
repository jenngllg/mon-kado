using JennGllg.Fr.MonKado.Back.Api.Middleware;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using System.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenActivityIsMissing_ReturnsHttpContextTraceIdentifier()
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "http-context-trace-id";
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "client-correlation-id";
        var middleware = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance);
        var previousActivity = Activity.Current;
        Activity.Current = null;

        // Act
        try
        {
            await middleware.InvokeAsync(context);
        }
        finally
        {
            Activity.Current = previousActivity;
        }

        // Assert
        var returnedValue = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.Equal(
            context.TraceIdentifier,
            returnedValue);
        Assert.NotEqual(
            "client-correlation-id",
            returnedValue);
    }

    [Fact]
    public async Task InvokeAsync_WhenActivityExists_UsesActivityTraceIdentifier()
    {
        // Arrange
        using var activity = new Activity("test");
        activity.Start();
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(
            activity.TraceId.ToString(),
            context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString());
    }
}
