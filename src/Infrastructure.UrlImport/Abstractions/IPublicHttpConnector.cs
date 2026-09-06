namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;

/// <summary>Validates DNS and pins each HTTP connection to a public address.</summary>
public interface IPublicHttpConnector
{
    /// <summary>Creates a safe stream for the HTTP handler.</summary>
    /// <param name="host">The requested hostname.</param>
    /// <param name="port">The standard HTTP destination port.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A stream connected only to a validated public address.</returns>
    ValueTask<Stream> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken);
}
