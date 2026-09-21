using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Api.Services;

using Microsoft.AspNetCore.Http;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Services;

public class GeneralRequestLimiterTests
{
    [Fact]
    public void AttemptAcquire_WhenDefaultWindowIsExhausted_RejectsRequest301Immediately()
    {
        // Arrange
        using var limiter = new GeneralRequestLimiter(Microsoft.Extensions.Options.Options.Create(new GeneralRateLimitOptions()));
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.1");

        // Act
        var leases = Enumerable.Range(
                0,
                301)
            .Select(_ => limiter.AttemptAcquire(context))
            .ToArray();

        // Assert
        try
        {
            Assert.All(
                leases.Take(300),
                lease => Assert.True(lease.IsAcquired));
            Assert.False(leases[300].IsAcquired);
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    [Theory]
    [InlineData("192.0.2.1", "::ffff:192.0.2.1", false)]
    [InlineData("192.0.2.1", "192.0.2.2", true)]
    [InlineData("2001:db8::1", "2001:db8::2", true)]
    [InlineData(null, null, false)]
    public void AttemptAcquire_WhenAddressesAreNormalized_SharesOnlyMatchingPartition(
        string? first,
        string? second,
        bool acquired)
    {
        // Arrange
        using var limiter = new GeneralRequestLimiter(Microsoft.Extensions.Options.Options.Create(new GeneralRateLimitOptions { PermitLimit = 1 }));
        var firstContext = new DefaultHttpContext();
        firstContext.Connection.RemoteIpAddress = first is null ? null : IPAddress.Parse(first);
        var secondContext = new DefaultHttpContext();
        secondContext.Connection.RemoteIpAddress = second is null ? null : IPAddress.Parse(second);

        // Act
        using var initial = limiter.AttemptAcquire(firstContext);
        using var next = limiter.AttemptAcquire(secondContext);

        // Assert
        Assert.True(initial.IsAcquired);
        Assert.Equal(
            acquired,
            next.IsAcquired);
    }
}
