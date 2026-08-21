using JennGllg.Fr.MonKado.Back.Api.Middleware;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

using System.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class CorrelationIdMiddlewareTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("invalid", false)]
    [InlineData("00000000-0000-0000-0000-000000000000", false)]
    [InlineData("0198d027-51c0-7000-8000-000000000001", true)]
    public async Task InvokeAsync_WhenCorrelationHeaderIsProvided_ReturnsExpectedIdentifier(
        string? providedValue,
        bool preservesValue)
    {
        // Arrange
        var context = new DefaultHttpContext();

        if (providedValue is not null)
            context.Request.Headers[CorrelationIdMiddleware.HeaderName] = providedValue;

        var middleware = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            NullLogger<CorrelationIdMiddleware>.Instance);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var returnedValue = context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.True(Guid.TryParse(
            returnedValue,
            out var returnedIdentifier));
        Assert.NotEqual(
            Guid.Empty,
            returnedIdentifier);
        Assert.Equal(
            preservesValue ? providedValue : returnedValue,
            returnedValue);

        if (!preservesValue && providedValue is not null)
            Assert.NotEqual(
                providedValue,
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
        Assert.NotEqual(
            default,
            activity.TraceId);
    }
}
