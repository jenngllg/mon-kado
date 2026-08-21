using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;
/// <summary>
/// Defines the contract for account session service.
/// </summary>

public interface IAccountSessionService
{
    /// <summary>
    /// Executes the login async operation.
    /// </summary>
    /// <param name="email">The email.</param>
    /// <param name="password">The password.</param>
    /// <param name="rememberMe">The remember me.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task<AccountLoginResult> LoginAsync(
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken);
}
