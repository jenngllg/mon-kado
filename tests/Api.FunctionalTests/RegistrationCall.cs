namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RegistrationCall(
    string email,
    string password,
    string displayName)
{
    public string Email { get; } = email;

    public string Password { get; } = password;

    public string DisplayName { get; } = displayName;
}
