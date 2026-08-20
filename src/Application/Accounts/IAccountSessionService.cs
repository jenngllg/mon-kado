namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public interface IAccountSessionService
{
    Task<AccountLoginResult> LoginAsync(
        string email,
        string password,
        bool rememberMe,
        CancellationToken cancellationToken);
}

public enum AccountLoginResult
{
    Success,
    InvalidCredentials,
    EmailNotConfirmed
}
