using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Identifies the shared wish to copy; its content is read by the server.</summary>
/// <param name="sourceShareLinkId">The source share-link identifier.</param>
/// <param name="sourceWishId">The source wish identifier.</param>
[ExcludeFromCodeCoverage]
public class CopyWishRequest(
    Guid? sourceShareLinkId,
    Guid? sourceWishId)
{
    /// <summary>Gets the nullable source share-link identifier.</summary>
    public Guid? SourceShareLinkId { get; } = sourceShareLinkId;
    /// <summary>Gets the nullable source wish identifier.</summary>
    public Guid? SourceWishId { get; } = sourceWishId;
}
