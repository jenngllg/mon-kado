namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Purges expired recipients and retained audits independently of provider enablement.</summary>
public interface IAccountErasureMaintenance
{
    /// <summary>Performs one bounded idempotent cleanup cycle.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed after the cleanup statements commit.</returns>
    Task PurgeAsync(CancellationToken cancellationToken);
}
