using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Requests suggestions from a public merchant URL.</summary>
/// <param name="url">The merchant URL to analyze.</param>
[ExcludeFromCodeCoverage]
public class CreateWishImportPreviewRequest(string? url)
{
    /// <summary>Gets the merchant URL.</summary>
    public string? Url { get; } = url;
}
