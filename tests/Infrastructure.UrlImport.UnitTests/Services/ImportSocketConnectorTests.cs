using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

using System.Net;
using System.Net.Sockets;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Services;

public class ImportSocketConnectorTests
{
    [Fact]
    public async Task ConnectAsync_WhenListenerIsAvailable_ReturnsOwningStream()
    {
        // Arrange
        using var listener = new TcpListener(
            IPAddress.Loopback,
            0);
        listener.Start();
        var connector = new ImportSocketConnector();
        var cancellationToken = TestContext.Current.CancellationToken;
        var accepted = listener.AcceptTcpClientAsync(cancellationToken);

        // Act
        await using var stream = await connector.ConnectAsync(
            (IPEndPoint)listener.LocalEndpoint,
            cancellationToken);
        using var connection = await accepted;

        // Assert
        Assert.True(stream.CanRead);
        Assert.True(stream.CanWrite);
    }

    [Fact]
    public async Task ConnectAsync_WhenCanceled_ClosesSocketAndPropagatesCancellation()
    {
        // Arrange
        var connector = new ImportSocketConnector();
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await source.CancelAsync();

        // Act
        var action = () => connector.ConnectAsync(
            new IPEndPoint(
                IPAddress.Loopback,
                1),
            source.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
    }
}
