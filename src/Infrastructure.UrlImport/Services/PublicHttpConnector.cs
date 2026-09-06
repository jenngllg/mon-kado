using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

/// <summary>Prevents DNS rebinding by connecting to the exact validated address.</summary>
/// <param name="resolver">The DNS resolver.</param>
/// <param name="connector">The numerical socket connector.</param>
public class PublicHttpConnector(
    IImportDnsResolver resolver,
    IImportSocketConnector connector) : IPublicHttpConnector
{
    /// <inheritdoc/>
    public async ValueTask<Stream> ConnectAsync(
        string host,
        int port,
        CancellationToken cancellationToken)
    {
        var addresses = await resolver.ResolveAsync(
            host,
            cancellationToken);

        if (addresses.Length == 0 || addresses.Any(address => !PublicAddressPolicy.IsPublic(address)))
            throw new WishImportUrlRejectedException();

        return await connector.ConnectAsync(
            new IPEndPoint(
                addresses[0],
                port),
            cancellationToken);
    }
}
