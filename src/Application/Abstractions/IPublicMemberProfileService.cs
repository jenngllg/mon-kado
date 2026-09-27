using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Reads confirmed members and their discoverable, actively shared lists.</summary>
public interface IPublicMemberProfileService
{
    /// <summary>Reads the public profile without exposing private account or list data.</summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The public profile, or null when the member is unavailable.</returns>
    Task<PublicMemberProfile?> GetAsync(
        Guid memberId,
        CancellationToken cancellationToken);
}
