using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;

/// <summary>
/// Describes the result of an authentication attempt and its associated user when available.
/// </summary>
/// <param name="result">The authentication result.</param>
/// <param name="user">The authenticated user, when available.</param>
public class AuthenticationAttempt(
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
