namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;
/// <summary>
/// Defines the contract for email confirmation service.
/// </summary>

public interface IEmailConfirmationService
{
    /// <summary>
    /// Executes the confirm async operation.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <param name="token">The token.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task<bool> ConfirmAsync(
        string userId,
        string token,
        CancellationToken cancellationToken);
    /// <summary>
    /// Executes the request async operation.
    /// </summary>
    /// <param name="email">The email.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    Task RequestAsync(
        string email,
        CancellationToken cancellationToken);
}
