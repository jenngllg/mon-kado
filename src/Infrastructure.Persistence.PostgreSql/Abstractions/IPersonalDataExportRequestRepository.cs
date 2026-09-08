using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>Coordinates shared export reuse and quotas inside a caller-owned account-locked transaction.</summary>
public interface IPersonalDataExportRequestRepository
{
    /// <summary>Locks actor and target accounts in identifier order until the caller commits.</summary>
    /// <param name="administratorId">The acting account.</param>
    /// <param name="memberId">The target account.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing acquisition of all existing account locks.</returns>
    Task LockAccountsAsync(
        Guid administratorId,
        Guid memberId,
        CancellationToken cancellationToken);
    /// <summary>Reuses an active request or stages one new request without saving changes.</summary>
    /// <param name="memberId">The account already locked and authorized by the caller.</param>
    /// <param name="now">The UTC request time.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tracked export to save with any caller-owned audit event.</returns>
    Task<MemberDataExport> GetOrCreateAsync(
        Guid memberId,
        DateTime now,
        CancellationToken cancellationToken);
}
