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
        context.TraceIdentifier = "http-context-trace-id";

        if (providedValue is not null)
            context.Request.Headers[CorrelationIdMiddleware.HeaderName] = providedValue;

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
        var identifierIsValid = Guid.TryParse(
            returnedValue,
            out var returnedIdentifier);
        Assert.True(identifierIsValid);
        Assert.NotEqual(
            Guid.Empty,
            returnedIdentifier);

        if (preservesValue)
        {
            Assert.Equal(
                providedValue,
                returnedValue);

            return;
        }

        Assert.Equal(
            7,
            returnedIdentifier.Version);

        if (providedValue is not null)
            Assert.NotEqual(
                providedValue,
                returnedValue);
    }

    [Fact]
    public async Task InvokeAsync_WhenActivityExists_KeepsCorrelationIdentifierDistinctFromTraceIdentifier()
    {
        // Arrange
        const string CorrelationId = "0198d027-51c0-7000-8000-000000000001";
        using var activity = new Activity("test");
        activity.Start();
        var traceId = activity.TraceId.ToString();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = CorrelationId;
        var logger = new ScopeCapturingLogger<CorrelationIdMiddleware>();
        var middleware = new CorrelationIdMiddleware(
            _ => Task.CompletedTask,
            logger);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        var returnedCorrelationId =
            context.Response.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        Assert.NotNull(logger.Scope);
        Assert.Equal(
            CorrelationId,
            returnedCorrelationId);
        Assert.Equal(
            CorrelationId,
            logger.Scope["CorrelationId"]);
        Assert.Equal(
            traceId,
            logger.Scope["TraceId"]);
    }
}
