using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Removes expired sessions, access-token metadata and retained session-revocation audits.
/// </summary>
/// <param name="sessionRepository">The authentication session repository.</param>
/// <param name="context">The scoped metadata cleanup context.</param>
public class ExpiredAuthenticationSessionCleanup(
    IAuthenticationSessionRepository sessionRepository,
    MonKadoDbContext context)
    : IExpiredAuthenticationSessionCleanup
{
    /// <summary>
    /// Purges expired token metadata and audits in batches, then deletes a bounded batch of expired sessions.
    /// </summary>
    /// <param name="cutoff">The cutoff.</param>
    /// <param name="batchSize">The batch size.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The number of deleted sessions.</returns>
    public async Task<int> DeleteExpiredSessionsAsync(
        DateTime cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        var tokenCutoff = cutoff.AddSeconds(-AccessTokenConstraints.ClockSkewSeconds);
        var deleted = 0;
        do
        {
            var identifiers = context.AuthenticationAccessTokens
                .Where(token => token.ExpiresAt < tokenCutoff)
                .OrderBy(token => token.ExpiresAt)
                .ThenBy(token => token.Id)
                .Take(batchSize)
                .Select(token => token.Id);
            deleted = await context.AuthenticationAccessTokens
                .Where(token => identifiers.Contains(token.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }
        while (deleted == batchSize);
        do
        {
            var identifiers = context.AdministrativeSessionRevocationEvents
                .Where(entry => entry.CreatedAt.AddMonths(AdministrativeSessionRevocationConstraints.AuditRetentionMonths) <= cutoff)
                .OrderBy(entry => entry.CreatedAt)
                .ThenBy(entry => entry.Id)
                .Take(batchSize)
                .Select(entry => entry.Id);
            deleted = await context.AdministrativeSessionRevocationEvents
                .Where(entry => identifiers.Contains(entry.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }
        while (deleted == batchSize);

        return await sessionRepository.DeleteExpiredAsync(
            cutoff,
            batchSize,
            cancellationToken);
    }
}
