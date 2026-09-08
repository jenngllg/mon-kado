namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Executes an independently verified erasure request with durable accountability.</summary>
public interface IAdministrativeAccountErasureService
{
    /// <summary>Erases another account and durably schedules cleanup and notification.</summary>
    /// <param name="administratorId">The authenticated administrator.</param>
    /// <param name="memberId">The target account.</param>
    /// <param name="requestReference">The normalized external request reference.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The durable erasure operation identifier.</returns>
    Task<Guid> ExecuteAsync(
        Guid administratorId,
        Guid memberId,
        string requestReference,
        CancellationToken cancellationToken);
}
