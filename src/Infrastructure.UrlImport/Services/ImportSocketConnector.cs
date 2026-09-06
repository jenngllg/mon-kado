using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;

using System.Net;
using System.Net.Sockets;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

/// <summary>Creates owning TCP streams for validated import connections.</summary>
public class ImportSocketConnector : IImportSocketConnector
{
    /// <inheritdoc/>
    public async Task<Stream> ConnectAsync(
        IPEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        var socket = new Socket(
            endpoint.AddressFamily,
            SocketType.Stream,
            ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(
                endpoint,
                cancellationToken);

            return new NetworkStream(
                socket,
                ownsSocket: true);
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }
}
