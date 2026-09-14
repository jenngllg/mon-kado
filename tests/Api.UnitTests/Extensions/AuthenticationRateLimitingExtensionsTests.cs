using JennGllg.Fr.MonKado.Back.Api.Attributes;
using JennGllg.Fr.MonKado.Back.Api.Extensions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Extensions;

public class AuthenticationRateLimitingExtensionsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddAuthenticationRateLimiting_WhenFirstFactorEndpointsAlternate_SharesQuotaOnlyForMarkedRequests(bool marked)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthenticationRateLimiting();
        await using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RateLimiterOptions>>().Value;
        var limiter = options.GlobalLimiter;
        Assert.NotNull(limiter);
        var paths = new[]
        {
            "/api/v1/auth/sessions",
            "/api/v1/auth/google/completions",
            "/api/v1/auth/google/link",
            "/api/v1/members/current/two-factor/reauthentications"
        };

        // Act
        for (var attempt = 0; attempt < AuthenticationRateLimitingExtensions.TwoFactorChallengeStartPermitLimit; attempt++)
        {
            using var lease = await limiter.AcquireAsync(
                CreateContext(
                    paths[attempt % paths.Length],
                    marked,
                    IPAddress.Loopback),
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.True(lease.IsAcquired);
        }

        using var extra = await limiter.AcquireAsync(
            CreateContext(
                paths[0],
                marked,
                IPAddress.Loopback),
            cancellationToken: TestContext.Current.CancellationToken);
        using var otherAddress = await limiter.AcquireAsync(
            CreateContext(
                paths[0],
                marked,
                IPAddress.IPv6Loopback),
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            !marked,
            extra.IsAcquired);
        Assert.True(otherAddress.IsAcquired);
    }

    private static DefaultHttpContext CreateContext(
        string path,
        bool marked,
        IPAddress address)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = address;

        if (marked)
            context.SetEndpoint(new Endpoint(
                null,
                new EndpointMetadataCollection(new StartsTwoFactorChallengeAttribute()),
                path));

        return context;
    }
}
