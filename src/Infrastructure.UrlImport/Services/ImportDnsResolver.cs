using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

/// <summary>Uses cancellable system DNS for import destinations.</summary>
public class ImportDnsResolver : IImportDnsResolver
{
    /// <inheritdoc/>
    public Task<IPAddress[]> ResolveAsync(
        string host,
        CancellationToken cancellationToken)
    {

        return Dns.GetHostAddressesAsync(
            host,
            cancellationToken);
    }
}
