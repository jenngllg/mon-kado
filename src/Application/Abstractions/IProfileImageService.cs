using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Coordinates durable profile-photo references and deferred file deletion.</summary>
public interface IProfileImageService
{
    /// <summary>Adds or replaces the authenticated member's photo atomically.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="imageId">The newly stored image identifier.</param>
    /// <param name="contentHash">The normalized content hash.</param>
    /// <param name="expectedVersion">The expected account version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete updated profile.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The confirmed member no longer exists.</exception>
    /// <exception cref="MemberProfileVersionConflictException">The account version is stale.</exception>
    /// <exception cref="DependencyUnavailableException">The durable mutation cannot be confirmed.</exception>
    Task<MemberProfile> UpsertAsync(
        Guid memberId,
        Guid imageId,
        byte[] contentHash,
        uint expectedVersion,
        CancellationToken cancellationToken);
    /// <summary>Removes the authenticated member's photo and queues its file for deletion.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="expectedVersion">The expected account version.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete updated profile.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The confirmed member no longer exists.</exception>
    /// <exception cref="MemberProfileVersionConflictException">The account version is stale.</exception>
    /// <exception cref="ProfileImageNotFoundException">The current profile has no photo.</exception>
    /// <exception cref="DependencyUnavailableException">The durable removal cannot be confirmed.</exception>
    Task<MemberProfile> DeleteAsync(
        Guid memberId,
        uint expectedVersion,
        CancellationToken cancellationToken);
    /// <summary>Revalidates a public photo against the confirmed account's current state.</summary>
    /// <param name="memberId">The public member identifier.</param>
    /// <param name="imageId">The requested image identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the photo is currently available publicly.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<bool> IsCurrentAsync(
        Guid memberId,
        Guid imageId,
        CancellationToken cancellationToken);
}
