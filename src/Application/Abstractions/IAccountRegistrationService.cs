namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;
/// <summary>
/// Defines the contract for account registration service.
/// </summary>

public interface IAccountRegistrationService
{
    /// <summary>
    /// Executes the register async operation.
    /// </summary>
    /// <param name="email">The email.</param>
    /// <param name="password">The password.</param>
    /// <param name="displayName">The display name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task RegisterAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken);
}
