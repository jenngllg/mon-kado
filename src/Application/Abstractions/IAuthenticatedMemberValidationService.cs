namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Revalidates the durable member identity behind a cryptographically valid access token.</summary>
public interface IAuthenticatedMemberValidationService
{
    /// <summary>Rejects unknown tokens and tokens belonging to deleted members or inactive sessions.</summary>
    /// <param name="memberId">The validated token subject.</param>
    /// <param name="tokenId">The cryptographically validated JWT identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the durable token, session and member validation.</returns>
    /// <exception cref="Common.Exceptions.InvalidAccessTokenException">The registered token or its session is no longer valid.</exception>
    /// <exception cref="Common.Exceptions.DependencyUnavailableException">PostgreSQL cannot complete validation.</exception>
    Task ValidateAsync(
        Guid memberId,
        Guid tokenId,
        CancellationToken cancellationToken);
}
