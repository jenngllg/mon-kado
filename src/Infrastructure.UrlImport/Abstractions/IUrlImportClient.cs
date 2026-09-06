using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;

/// <summary>Fetches bounded documents through a public-only HTTP transport.</summary>
public interface IUrlImportClient
{
    /// <summary>Downloads one document without automatic redirects or retries.</summary>
    /// <param name="url">The candidate absolute URL.</param>
    /// <param name="maximumBytes">The maximum decompressed response size.</param>
    /// <param name="cancellationToken">The shared import cancellation token.</param>
    /// <returns>The bounded document.</returns>
    Task<ImportDocument> DownloadAsync(
        Uri url,
        int maximumBytes,
        CancellationToken cancellationToken);
}
