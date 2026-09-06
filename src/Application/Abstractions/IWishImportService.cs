using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Prepares editable gift suggestions from a public merchant page.</summary>
public interface IWishImportService
{
    /// <summary>Downloads bounded metadata and an optional normalized image.</summary>
    /// <param name="url">The validated absolute product URL.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Suggestions and warnings without persisting a gift.</returns>
    Task<WishImportPreview> PreviewAsync(
        string url,
        CancellationToken cancellationToken);
}
