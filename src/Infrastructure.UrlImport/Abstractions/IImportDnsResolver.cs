using System.Net;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;

/// <summary>Resolves merchant destinations without initiating HTTP requests.</summary>
public interface IImportDnsResolver
{
    /// <summary>Resolves all addresses for one hostname.</summary>
    /// <param name="host">The destination hostname.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>All resolved IPv4 and IPv6 addresses.</returns>
    Task<IPAddress[]> ResolveAsync(
        string host,
        CancellationToken cancellationToken);
}
