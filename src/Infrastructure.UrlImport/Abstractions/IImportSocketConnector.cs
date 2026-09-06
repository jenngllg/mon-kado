using System.Net;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;

/// <summary>Connects to an already validated numerical address without resolving DNS again.</summary>
public interface IImportSocketConnector
{
    /// <summary>Opens a TCP stream to an exact address.</summary>
    /// <param name="endpoint">The validated address and port.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The owning network stream.</returns>
    Task<Stream> ConnectAsync(
        IPEndPoint endpoint,
        CancellationToken cancellationToken);
}
