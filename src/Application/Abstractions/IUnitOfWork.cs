namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;
/// <summary>
/// Defines the contract for unit of work.
/// </summary>

public interface IUnitOfWork
{
    /// <summary>
    /// Executes the save changes async operation.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
