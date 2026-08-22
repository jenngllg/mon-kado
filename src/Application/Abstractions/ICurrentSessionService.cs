using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Loads the current authenticated member session.
/// </summary>
public interface ICurrentSessionService
{
    /// <summary>
    /// Gets the current session from persistence.
    /// </summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current session when the member still exists; otherwise, <see langword="null" />.</returns>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<CurrentSession?> GetAsync(
        Guid memberId,
        CancellationToken cancellationToken);
}
