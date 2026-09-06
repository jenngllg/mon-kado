namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>Protects confirmation tokens bound to an account and a durable deletion request.</summary>
public interface IMemberAccountDeletionTokenService
{
    /// <summary>Protects the request identity for delivery to the member.</summary>
    /// <param name="memberId">The member identifier.</param>
    /// <param name="requestId">The deletion request identifier.</param>
    /// <returns>The opaque protected token.</returns>
    string Create(
        Guid memberId,
        Guid requestId);
    /// <summary>Resolves a token only for its intended authenticated member.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="token">The protected token.</param>
    /// <returns>The request identifier, or null for an invalid token.</returns>
    Guid? Read(
        Guid memberId,
        string token);
}
