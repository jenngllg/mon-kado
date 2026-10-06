using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Contains only the owner-controlled wishlist archive state.</summary>
/// <param name="isArchived">The requested archive state.</param>
[ExcludeFromCodeCoverage]
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public class SetWishlistArchivedRequest(bool? isArchived)
{
    /// <summary>Gets the archive state; the common validator rejects omission and null.</summary>
    public bool? IsArchived { get; } = isArchived;
}
