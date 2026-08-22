using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Updates authenticated member profiles.
/// </summary>
public interface IMemberProfileService
{
    /// <summary>
    /// Updates the member display name when the expected profile version is current.
    /// </summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="displayName">The normalized display name.</param>
    /// <param name="expectedVersion">The profile version supplied by the client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The updated profile when the member exists; otherwise, <see langword="null" />.</returns>
    /// <exception cref="Common.Exceptions.MemberProfileVersionConflictException">
    /// The supplied version is stale.
    /// </exception>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<MemberProfile?> UpdateAsync(
        Guid memberId,
        string displayName,
        uint expectedVersion,
        CancellationToken cancellationToken);
}
