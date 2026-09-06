using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Middleware;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using System.Security.Claims;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Middleware;

public class GiftImageRateLimitIdentityMiddlewareTests
{
    [Theory]
    [InlineData("missingEndpoint")]
    [InlineData("missingPolicy")]
    [InlineData("otherPolicy")]
    public async Task InvokeAsync_WhenUploadPolicyIsAbsent_SkipsAuthentication(string scenario)
    {
        // Arrange
        var nextWasCalled = false;
        var middleware = new GiftImageRateLimitIdentityMiddleware(_ =>
        {
            nextWasCalled = true;

            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();

        if (scenario != "missingEndpoint")
        {
            var metadata = scenario == "missingPolicy"
                ? new EndpointMetadataCollection()
                : new EndpointMetadataCollection(new EnableRateLimitingAttribute("Other"));
            context.SetEndpoint(new Endpoint(
                _ => Task.CompletedTask,
                metadata,
                "Test endpoint"));
        }

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextWasCalled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_WhenUploadPolicyIsPresent_UsesValidatedPrincipal(
        bool authenticationSucceeds)
    {
        // Arrange
        var nextWasCalled = false;
        var authenticationServiceMock = new Mock<IAuthenticationService>(MockBehavior.Strict);
        var middleware = new GiftImageRateLimitIdentityMiddleware(_ =>
        {
            nextWasCalled = true;

            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        var originalPrincipal = context.User;
        var authenticatedPrincipal = new ClaimsPrincipal(
            new ClaimsIdentity(
                [
                    new Claim(
                        ClaimTypes.NameIdentifier,
                        Guid.CreateVersion7().ToString("D"))
                ],
                JwtBearerDefaults.AuthenticationScheme));
        var result = authenticationSucceeds
            ? AuthenticateResult.Success(new AuthenticationTicket(
                authenticatedPrincipal,
                JwtBearerDefaults.AuthenticationScheme))
            : AuthenticateResult.Fail("Invalid token");
        authenticationServiceMock
            .Setup(service => service.AuthenticateAsync(
                context,
                JwtBearerDefaults.AuthenticationScheme))
            .ReturnsAsync(result);
        using var provider = new ServiceCollection()
            .AddSingleton(authenticationServiceMock.Object)
            .BuildServiceProvider();
        context.RequestServices = provider;
        context.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new EnableRateLimitingAttribute(
                AuthenticationRateLimitingExtensions.GiftImageUploadPolicy)),
            "Gift image upload"));

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(nextWasCalled);
        Assert.Same(
            authenticationSucceeds
                ? authenticatedPrincipal
                : originalPrincipal,
            context.User);
        authenticationServiceMock.Verify(
            service => service.AuthenticateAsync(
                context,
                JwtBearerDefaults.AuthenticationScheme),
            Times.Once);
        authenticationServiceMock.VerifyNoOtherCalls();
    }
}
