namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class LoginCall(
    string email,
    string password,
    bool rememberMe,
    string? currentRefreshToken)
{
    public string Email { get; } = email;

    public string Password { get; } = password;

    public bool RememberMe { get; } = rememberMe;

    public string? CurrentRefreshToken { get; } = currentRefreshToken;
}
