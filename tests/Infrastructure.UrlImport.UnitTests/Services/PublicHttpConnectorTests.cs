using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

using Moq;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Services;

public class PublicHttpConnectorTests
{
    private readonly Mock<IImportDnsResolver> _resolverMock;
    private readonly Mock<IImportSocketConnector> _socketConnectorMock;
    private readonly PublicHttpConnector _connector;
    public PublicHttpConnectorTests()
    {
        _resolverMock = new(MockBehavior.Strict);
        _socketConnectorMock = new(MockBehavior.Strict);
        _connector = new(
            _resolverMock.Object,
            _socketConnectorMock.Object);
    }

    [Theory]
    [InlineData("")]
    [InlineData("127.0.0.1")]
    [InlineData("1.1.1.1,127.0.0.1")]
    [InlineData("1.1.1.1,::1")]
    public async Task ConnectAsync_WhenDnsContainsUnsafeAddresses_NeverOpensSocket(string addresses)
    {
        // Arrange
        var resolved = addresses
            .Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries)
            .Select(IPAddress.Parse)
            .ToArray();
        var cancellationToken = TestContext.Current.CancellationToken;
        _resolverMock
            .Setup(resolver => resolver.ResolveAsync(
                "merchant.example",
                cancellationToken))
            .ReturnsAsync(resolved);

        // Act
        var exception = await Record.ExceptionAsync(async () => await _connector.ConnectAsync(
                "merchant.example",
                443,
                cancellationToken));

        // Assert
        Assert.IsType<WishImportUrlRejectedException>(exception);
        _resolverMock.Verify(
            resolver => resolver.ResolveAsync(
                "merchant.example",
                cancellationToken),
            Times.Once);
        _resolverMock.VerifyNoOtherCalls();
        _socketConnectorMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ConnectAsync_WhenDnsIsPublic_ConnectsToExactAddressWithOriginalToken()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var address = IPAddress.Parse("1.1.1.1");
        using var stream = new MemoryStream();
        _resolverMock
            .Setup(resolver => resolver.ResolveAsync(
                "merchant.example",
                cancellationToken))
            .ReturnsAsync([address]);
        _socketConnectorMock
            .Setup(connector => connector.ConnectAsync(
                It.Is<IPEndPoint>(endpoint => endpoint.Address.Equals(address) && endpoint.Port == 443),
                cancellationToken))
            .ReturnsAsync(stream);

        // Act
        var actual = await _connector.ConnectAsync(
            "merchant.example",
            443,
            cancellationToken);

        // Assert
        Assert.Same(
            stream,
            actual);
        _resolverMock.Verify(
            resolver => resolver.ResolveAsync(
                "merchant.example",
                cancellationToken),
            Times.Once);
        _socketConnectorMock.Verify(
            connector => connector.ConnectAsync(
                It.Is<IPEndPoint>(endpoint => endpoint.Address.Equals(address) && endpoint.Port == 443),
                cancellationToken),
            Times.Once);
        _resolverMock.VerifyNoOtherCalls();
        _socketConnectorMock.VerifyNoOtherCalls();
    }
}
