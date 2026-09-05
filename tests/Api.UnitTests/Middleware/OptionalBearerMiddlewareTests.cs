using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Middleware;

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Middleware;

public class OptionalBearerMiddlewareTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    public async Task InvokeAsync_WhenRequestMayContinue_InvokesNextDelegate(
        bool acceptsOptionalBearer,
        bool hasAuthorizationHeader,
        bool isAuthenticated,
        bool hasNonOptionalEndpoint)
    {
        // Arrange
        var nextWasCalled = false;
        var middleware = new OptionalBearerMiddleware(_ =>
        {
            nextWasCalled = true;

            return Task.CompletedTask;
        });
        var context = CreateContext(
            acceptsOptionalBearer,
            hasAuthorizationHeader,
            isAuthenticated,
            hasNonOptionalEndpoint: hasNonOptionalEndpoint);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextWasCalled);
        Assert.Equal(
            StatusCodes.Status200OK,
            context.Response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvokeAsync_WhenOptionalBearerIsInvalid_ReturnsUnauthorizedWithoutInvokingNextDelegate(
        bool hasIdentity)
    {
        // Arrange
        var nextWasCalled = false;
        var middleware = new OptionalBearerMiddleware(_ =>
        {
            nextWasCalled = true;

            return Task.CompletedTask;
        });
        var context = CreateContext(
            acceptsOptionalBearer: true,
            hasAuthorizationHeader: true,
            isAuthenticated: false,
            hasIdentity);

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.False(nextWasCalled);
        Assert.Equal(
            StatusCodes.Status401Unauthorized,
            context.Response.StatusCode);
    }

    private static DefaultHttpContext CreateContext(
        bool acceptsOptionalBearer,
        bool hasAuthorizationHeader,
        bool isAuthenticated,
        bool hasIdentity = false,
        bool hasNonOptionalEndpoint = false)
    {
        var context = new DefaultHttpContext();

        if (acceptsOptionalBearer)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new OptionalBearerAttribute()),
                "Optional Bearer"));
        }
        else if (hasNonOptionalEndpoint)
        {
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(),
                "Non-optional Bearer"));
        }

        if (hasAuthorizationHeader)
            context.Request.Headers.Authorization = "Bearer invalid";

        if (isAuthenticated)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString())
                ],
                HeaderNames.Authorization));
        }
        else if (hasIdentity)
        {
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
        }
        else
        {
            context.User = new ClaimsPrincipal();
        }

        return context;
    }
}
