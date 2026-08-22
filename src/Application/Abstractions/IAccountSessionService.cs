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
    /// <param name="currentRefreshToken">The refresh token currently held by the browser.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task<AccountSessionLoginResult> LoginAsync(
        string email,
        string password,
        bool rememberMe,
        string? currentRefreshToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the refresh session held by the current browser.
    /// </summary>
    /// <param name="refreshToken">The refresh token currently held by the browser.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task LogoutAsync(
        string? refreshToken,
        CancellationToken cancellationToken);

    /// <summary>
    /// Rotates an existing authentication session.
    /// </summary>
    /// <param name="refreshToken">The refresh token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rotated tokens when the session is valid; otherwise, <see langword="null" />.</returns>
    Task<AccountSessionTokens?> RefreshAsync(
        string refreshToken,
        CancellationToken cancellationToken);
}
