namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class PasswordResetCall(
    string userId,
    string token,
    string newPassword)
{
    public string UserId { get; } = userId;

    public string Token { get; } = token;

    public string NewPassword { get; } = newPassword;
}
