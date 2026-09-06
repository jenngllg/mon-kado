using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Checks current administrator privileges without trusting JWT role claims.</summary>
public interface IAdministratorAccessService
{
    /// <summary>Reads the current database-backed administrator access.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current account and administrator-role state.</returns>
    Task<AdministratorAccess> GetAccessAsync(
        Guid memberId,
        CancellationToken cancellationToken);
}
