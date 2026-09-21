using JennGllg.Fr.MonKado.Back.Api.Extensions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using System.Threading.RateLimiting;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Extensions;

public class RateLimitResponseExtensionsTests
{
    [Theory]
    [InlineData(false, "60")]
    [InlineData(true, "1")]
    public async Task WriteRejectionAsync_WhenMetadataIsMissingOrFractional_ReturnsBoundedRetryHeader(
        bool hasMetadata,
        string expected)
    {
        // Arrange
        var leaseMock = new Mock<RateLimitLease>(MockBehavior.Strict);
        object? metadata = hasMetadata ? TimeSpan.FromMilliseconds(100) : null;
        leaseMock.Setup(lease => lease.TryGetMetadata(
                MetadataName.RetryAfter.Name,
                out metadata))
            .Returns(hasMetadata);
        using var services = new ServiceCollection()
            .AddLogging()
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = new MemoryStream();

        // Act
        await RateLimitResponseExtensions.WriteRejectionAsync(
            context,
            leaseMock.Object,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expected,
            context.Response.Headers.RetryAfter);
        Assert.Equal(
            "no-store",
            context.Response.Headers.CacheControl);
        Assert.Equal(
            429,
            context.Response.StatusCode);
        leaseMock.Verify(lease => lease.TryGetMetadata(
                MetadataName.RetryAfter.Name,
                out metadata),
            Times.Once);
        leaseMock.VerifyNoOtherCalls();
    }
}
