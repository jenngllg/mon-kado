using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;

/// <summary>Extracts passive structured metadata without running merchant code.</summary>
public interface IMerchantMetadataExtractor
{
    /// <summary>Parses a bounded HTML document without network access.</summary>
    /// <param name="document">The downloaded merchant document.</param>
    /// <returns>Only unambiguous and valid suggestions.</returns>
    MerchantMetadata Extract(ImportDocument document);
}
