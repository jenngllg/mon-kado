namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Creates request-specific purposes for member email change tokens.
/// </summary>
public static class MemberEmailChangeTokenPurpose
{
    /// <summary>
    /// Creates a purpose for the supplied request and normalized email address.
    /// </summary>
    /// <param name="requestId">The email change request identifier.</param>
    /// <param name="normalizedEmail">The normalized requested email address.</param>
    /// <returns>The token purpose.</returns>
    public static string Create(
        Guid requestId,
        string normalizedEmail)
    {

        return $"MonKado.EmailChange:{requestId:D}:{normalizedEmail}";
    }
}
