namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Revalidates the durable member identity behind a cryptographically valid access token.</summary>
public interface IAuthenticatedMemberValidationService
{
    /// <summary>Rejects access tokens belonging to deleted members.</summary>
    /// <param name="memberId">The validated token subject.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the account existence check.</returns>
    Task ValidateAsync(
        Guid memberId,
        CancellationToken cancellationToken);
}
