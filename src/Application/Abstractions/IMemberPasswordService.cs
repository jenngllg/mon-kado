namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Defines member password operations.
/// </summary>
public interface IMemberPasswordService
{
    /// <summary>
    /// Changes a member password and revokes the member security sessions.
    /// </summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="currentPassword">The current password.</param>
    /// <param name="newPassword">The new password.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the member exists; otherwise, <see langword="false" />.</returns>
    /// <exception cref="Common.Exceptions.CurrentPasswordInvalidException">The current password is invalid.</exception>
    /// <exception cref="Common.Exceptions.RequestValidationException">Identity rejects the new password.</exception>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    Task<bool> ChangeAsync(
        Guid memberId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken);
}
