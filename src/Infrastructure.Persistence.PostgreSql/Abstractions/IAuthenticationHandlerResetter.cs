namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
/// <summary>
/// Defines the contract for authentication handler resetter.
/// </summary>

public interface IAuthenticationHandlerResetter
{
    /// <summary>
    /// Executes the reset operation.
    /// </summary>
    /// <param name="authenticationScheme">The authentication scheme.</param>
    void Reset(string authenticationScheme);
}
