using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Requests and completes anonymous account password resets.
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// Requests a password reset email without revealing whether the account is eligible.
    /// </summary>
    /// <param name="email">The submitted account email address after trimming.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task RequestAsync(
        string email,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resets an eligible account password using its single-use reset link.
    /// </summary>
    /// <param name="userId">The account identifier from the reset link.</param>
    /// <param name="token">The password reset token.</param>
    /// <param name="newPassword">The new account password.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the password was reset; otherwise, <see langword="false" />.</returns>
    /// <exception cref="RequestValidationException">Identity rejects the new password.</exception>
    /// <exception cref="PasswordResetInvalidException">Identity detects a concurrent password reset.</exception>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<bool> ResetAsync(
        string userId,
        string token,
        string newPassword,
        CancellationToken cancellationToken);
}
