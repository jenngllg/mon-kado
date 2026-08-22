using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Loads the current authenticated member session from PostgreSQL.
/// </summary>
/// <param name="memberRepository">The member repository.</param>
public class CurrentSessionService(IMemberRepository memberRepository)
    : ICurrentSessionService
{
    /// <summary>
    /// Gets the current authenticated member session.
    /// </summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The current session when the member exists; otherwise, <see langword="null" />.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    public async Task<CurrentSession?> GetAsync(
        Guid memberId,
        CancellationToken cancellationToken)
    {
        try
        {

            return await memberRepository.GetCurrentSessionAsync(
                memberId,
                cancellationToken);
        }
        catch (Exception exception) when (PostgreSqlFailureClassifier.IsUnavailable(exception))
        {

            throw new DependencyUnavailableException(
                "PostgreSQL",
                exception);
        }
    }
}
