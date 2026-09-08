namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Delivers bounded batches of post-erasure notifications.</summary>
public interface IAccountErasureEmailDispatcher
{
    /// <summary>Processes eligible fenced attempts without holding a provider-spanning transaction.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of claimed attempts.</returns>
    Task<int> DispatchAsync(CancellationToken cancellationToken);
}
