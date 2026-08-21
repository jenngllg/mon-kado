using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;

internal class AuthenticationAttempt(
    AccountLoginResult result,
    MonKadoUser? user)
{
    /// <summary>
    /// Gets invalid credentials.
    /// </summary>
    public static AuthenticationAttempt InvalidCredentials
    {
        get;
    } = new(
        AccountLoginResult.InvalidCredentials,
        null);
    /// <summary>
    /// Gets result.
    /// </summary>

    public AccountLoginResult Result { get; } = result;
    /// <summary>
    /// Gets user.
    /// </summary>

    public MonKadoUser? User { get; } = user;
}
