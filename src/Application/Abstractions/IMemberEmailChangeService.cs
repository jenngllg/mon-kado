namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>
/// Manages authenticated member email changes.
/// </summary>
public interface IMemberEmailChangeService
{
    /// <summary>
    /// Requests a member email change after verifying the current password and version.
    /// </summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="email">The normalized requested email address.</param>
    /// <param name="currentPassword">The current member password.</param>
    /// <param name="expectedVersion">The member version supplied by the client.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the member exists; otherwise, <see langword="false" />.</returns>
    Task<bool> RequestAsync(
        Guid memberId,
        string email,
        string currentPassword,
        uint expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>
    /// Confirms a pending member email change.
    /// </summary>
    /// <param name="requestId">The email change request identifier.</param>
    /// <param name="token">The encoded confirmation token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><see langword="true" /> when the request is confirmed; otherwise, <see langword="false" />.</returns>
    Task<bool> ConfirmAsync(
        Guid requestId,
        string token,
        CancellationToken cancellationToken);
}
