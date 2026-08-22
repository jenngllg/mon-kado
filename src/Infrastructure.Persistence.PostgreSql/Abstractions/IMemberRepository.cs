using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>
/// Defines persistence operations for member identity and roles.
/// </summary>
public interface IMemberRepository
{
    /// <summary>
    /// Adds the built-in Member role to a member in the current unit of work.
    /// </summary>
    /// <param name="memberId">The member identifier.</param>
    void AddMemberRole(Guid memberId);

    /// <summary>
    /// Gets the current member session without tracking persistence entities.
    /// </summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current session when the member exists; otherwise, <see langword="null" />.</returns>
    Task<CurrentSession?> GetCurrentSessionAsync(
        Guid memberId,
        CancellationToken cancellationToken);
}
