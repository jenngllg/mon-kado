using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Rejects unregistered JWTs and revoked sessions without caching authorization state.</summary>
/// <param name="context">The request-scoped database context.</param>
/// <param name="timeProvider">The UTC clock.</param>
public class AuthenticatedMemberValidationService(
    MonKadoDbContext context,
    TimeProvider timeProvider) : IAuthenticatedMemberValidationService
{
    /// <inheritdoc/>
    public async Task ValidateAsync(
        Guid memberId,
        Guid tokenId,
        CancellationToken cancellationToken)
    {
        try
        {
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var cutoff = now.AddSeconds(-AccessTokenConstraints.ClockSkewSeconds);
            var exists = await context.AuthenticationAccessTokens
                .AsNoTracking()
                .AnyAsync(
                token => token.Id == tokenId && token.ExpiresAt >= cutoff &&
                    context.AuthenticationSessions.Any(session => session.Id == token.SessionId &&
                        session.UserId == memberId && session.RevokedAt == null && session.ExpiresAt > now) &&
                    context.Users.Any(member => member.Id == memberId),
                cancellationToken);

            if (!exists)
                throw new InvalidAccessTokenException();
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }
}
