using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Services;

public class ImportDnsResolverTests
{
    [Fact]
    public async Task ResolveAsync_WhenAddressIsLiteral_ReturnsItWithoutExternalDns()
    {
        // Arrange
        var resolver = new ImportDnsResolver();

        // Act
        var addresses = await resolver.ResolveAsync(
            "127.0.0.1",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            IPAddress.Loopback,
            Assert.Single(addresses));
    }
}
