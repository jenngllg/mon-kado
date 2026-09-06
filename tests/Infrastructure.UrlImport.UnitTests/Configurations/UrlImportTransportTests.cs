using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Configurations;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moq;

using System.IO.Compression;
using System.Net;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Configurations;

public class UrlImportTransportTests
{
    private readonly Mock<IImportDnsResolver> _resolverMock = new(MockBehavior.Strict);
    private readonly Mock<IImportSocketConnector> _connectorMock = new(MockBehavior.Strict);
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownloadAsync_WhenResponseIsCompressed_AppliesLimitToDecodedBytes(bool tooLarge)
    {
        // Arrange
        var html = new string(
            'x',
            tooLarge ? 4096 : 5);
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(
            compressed,
            CompressionMode.Compress,
            true))
            gzip.Write(Encoding.UTF8.GetBytes(html));
        var payload = compressed.ToArray();
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nContent-Encoding: gzip\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        using var stream = new ScriptedHttpStream([
                ..header,
                ..payload
            ]);
        SetupConnection(stream);
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IUrlImportClient>();

        // Act
        var exception = await Record.ExceptionAsync(async () =>
            {
                var document = await client.DownloadAsync(
                    new Uri("http://merchant.example/product"),
                    1024,
                    TestContext.Current.CancellationToken);
                Assert.Equal(
                    html,
                    Encoding.UTF8.GetString(document.Content));
            });

        // Assert
        if (tooLarge)
            Assert.IsType<HttpRequestException>(exception);
        else
            Assert.Null(exception);
        var request = Encoding.ASCII.GetString(stream.Request.ToArray());
        Assert.DoesNotContain(
            "Authorization:",
            request,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "Cookie:",
            request,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "MonKado-Import/1.0",
            request,
            StringComparison.Ordinal);
        VerifyConnection();
    }

    [Fact]
    public async Task DownloadAsync_WhenRedirectResolvesToPrivateAddress_NeverConnectsToPrivateTarget()
    {
        // Arrange
        using var stream = new ScriptedHttpStream(Encoding.ASCII.GetBytes("HTTP/1.1 302 Found\r\nLocation: http://private.example/secret\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"));
        SetupConnection(stream);
        _resolverMock
            .Setup(resolver => resolver.ResolveAsync(
                "private.example",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([IPAddress.Loopback]);
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IUrlImportClient>();

        // Act
        var action = () => client.DownloadAsync(
            new Uri("http://merchant.example/product"),
            1024,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishImportUrlRejectedException>(action);
        _resolverMock.Verify(
            resolver => resolver.ResolveAsync(
                "private.example",
                It.IsAny<CancellationToken>()),
            Times.Once);
        VerifyConnection();
    }

    private void SetupConnection(ScriptedHttpStream stream)
    {
        _resolverMock
            .Setup(resolver => resolver.ResolveAsync(
                "merchant.example",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([IPAddress.Parse("1.1.1.1")]);
        _connectorMock
            .Setup(connector => connector.ConnectAsync(
                It.Is<IPEndPoint>(endpoint => endpoint.Address.Equals(IPAddress.Parse("1.1.1.1")) && endpoint.Port == 80),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(stream);
    }

    private void VerifyConnection()
    {
        _resolverMock.Verify(
            resolver => resolver.ResolveAsync(
                "merchant.example",
                It.IsAny<CancellationToken>()),
            Times.Once);
        _connectorMock.Verify(
            connector => connector.ConnectAsync(
                It.Is<IPEndPoint>(endpoint => endpoint.Address.Equals(IPAddress.Parse("1.1.1.1")) && endpoint.Port == 80),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _resolverMock.VerifyNoOtherCalls();
        _connectorMock.VerifyNoOtherCalls();
    }

    private ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.ConfigureUrlImportInjection(new ConfigurationBuilder().Build());
        services.RemoveAll<IImportDnsResolver>();
        services.RemoveAll<IImportSocketConnector>();
        services.AddSingleton(_resolverMock.Object);
        services.AddSingleton(_connectorMock.Object);

        return services.BuildServiceProvider();
    }
}
