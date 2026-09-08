using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

/// <summary>Stores only the issuance metadata needed to revoke a signed access token.</summary>
[ExcludeFromCodeCoverage]
public class AuthenticationAccessToken
{
    /// <summary>Gets the JWT identifier, never its encoded value.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the renewable session owning this token.</summary>
    public Guid SessionId
    {
        get; init;
    }
    /// <summary>Gets the exact UTC issuance time.</summary>
    public DateTime IssuedAt
    {
        get; init;
    }
    /// <summary>Gets the exact UTC expiry, without clock tolerance.</summary>
    public DateTime ExpiresAt
    {
        get; init;
    }
}
