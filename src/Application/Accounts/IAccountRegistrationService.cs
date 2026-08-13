namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public interface IAccountRegistrationService
{
    Task RegisterAsync(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken);
}
