using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Contains an administrator's requested wishlist moderation state.</summary>
[ExcludeFromCodeCoverage]
public class UpdateWishlistModerationRequest
{
    /// <summary>Gets the required suspension state.</summary>
    public bool? IsSuspended
    {
        get; init;
    }
    /// <summary>Gets the required suspension reason, or null for reactivation.</summary>
    public string? Reason
    {
        get; init;
    }
}
