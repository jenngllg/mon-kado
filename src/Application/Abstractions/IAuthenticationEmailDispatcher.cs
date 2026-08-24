using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;
/// <summary>
/// Defines the contract for authentication email dispatcher.
/// </summary>

public interface IAuthenticationEmailDispatcher
{
    /// <summary>
    /// Executes the dispatch pending async operation.
    /// </summary>
    /// <param name="frontendOrigin">The frontend origin.</param>
    /// <param name="policy">The bounded delivery policy.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task<int> DispatchPendingAsync(
        Uri frontendOrigin,
        AuthenticationEmailDeliveryPolicy policy,
        CancellationToken cancellationToken);
}
