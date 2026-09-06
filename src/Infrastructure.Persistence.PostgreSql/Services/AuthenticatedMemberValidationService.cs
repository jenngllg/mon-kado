using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Rejects deleted accounts without trusting the remaining JWT lifetime.</summary>
/// <param name="userRepository">The member persistence repository.</param>
public class AuthenticatedMemberValidationService(IMonKadoUserRepository userRepository) : IAuthenticatedMemberValidationService
{
    /// <inheritdoc/>
    public async Task ValidateAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        try
        {
            var exists = await userRepository
                .Query()
                .AnyAsync(
                member => member.Id == memberId,
                cancellationToken);

            if (!exists)
                throw new InvalidAuthenticationSessionException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }
}
