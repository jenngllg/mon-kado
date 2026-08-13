namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public interface IEmailConfirmationService
{
    Task<bool> ConfirmAsync(
        string userId,
        string token,
        CancellationToken cancellationToken);

    Task RequestAsync(
        string email,
        CancellationToken cancellationToken);
}
