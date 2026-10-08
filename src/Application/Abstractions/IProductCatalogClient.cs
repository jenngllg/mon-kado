using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Reads exact product references from an explicitly configured public merchant catalog.</summary>
public interface IProductCatalogClient
{
    /// <summary>Checks whether the configured anonymous catalog supports this product URL.</summary>
    /// <param name="url">The merchant product URL.</param>
    /// <returns>Whether the URL has an exact supported catalog reference.</returns>
    bool CanHandle(Uri url);
    /// <summary>Retrieves one exact product without searching by name or guessing its variant.</summary>
    /// <param name="url">The supported merchant product URL.</param>
    /// <param name="cancellationToken">The shared overall import budget.</param>
    /// <returns>Unambiguous catalog metadata, or empty suggestions when no exact product exists.</returns>
    Task<CatalogProductMetadata> GetAsync(
        Uri url,
        CancellationToken cancellationToken);
}
