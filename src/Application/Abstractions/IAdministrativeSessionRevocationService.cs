namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Executes an independently verified session revocation request with durable accountability.</summary>
public interface IAdministrativeSessionRevocationService
{
    /// <summary>Disconnects another account and commits the audit without sending a notification.</summary>
    /// <param name="administratorId">The authenticated administrator.</param>
    /// <param name="memberId">The target account.</param>
    /// <param name="requestReference">The normalized external request reference.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The durable session revocation operation identifier.</returns>
    Task<Guid> ExecuteAsync(
        Guid administratorId,
        Guid memberId,
        string requestReference,
        CancellationToken cancellationToken);
}
