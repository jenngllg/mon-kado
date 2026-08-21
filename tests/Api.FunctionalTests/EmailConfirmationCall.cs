namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class EmailConfirmationCall(
    string userId,
    string token)
{
    public string UserId { get; } = userId;

    public string Token { get; } = token;
}
