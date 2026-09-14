using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Security.Cryptography;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Stages MFA-required sign-ins without issuing tokens or changing existing browser sessions.</summary>
/// <param name="context">The shared transaction context.</param>
/// <param name="administratorAccess">The current PostgreSQL role reader.</param>
/// <param name="cryptography">The credential and proof cryptography.</param>
/// <param name="scopeFactory">The independent commit-verification scope factory.</param>
/// <param name="timeProvider">The UTC clock.</param>
public class TwoFactorChallengeIssuer(
    MonKadoDbContext context,
    IAdministratorAccessService administratorAccess,
    ITwoFactorCryptography cryptography,
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider) : ITwoFactorChallengeIssuer
{
    /// <inheritdoc/>
    public async Task<TwoFactorChallengeResponse?> StageAsync(
        MonKadoUser member,
        Guid challengeId,
        bool isPersistent,
        Guid? previousSessionId,
        CancellationToken cancellationToken)
    {
        var factor = await context.MemberTwoFactors.SingleOrDefaultAsync(
            candidate => candidate.MemberId == member.Id,
            cancellationToken);

        if (member.TwoFactorEnabled && factor?.CredentialId is null)
            throw new TwoFactorUnavailableException();

        if (factor?.CredentialId is null && await administratorAccess.GetAccessAsync(
                member.Id,
                cancellationToken) != AdministratorAccess.Granted)
            return null;

        if (factor is null)
        {
            factor = MemberTwoFactor.Create(member.Id);
            context.MemberTwoFactors.Add(factor);
        }

        var flow = cryptography.CreateFlow();
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var challenge = TwoFactorChallenge.CreateSignIn(
            challengeId,
            member.Id,
            flow.Hash,
            factor.CredentialId,
            SHA256.HashData(Encoding.UTF8.GetBytes(member.SecurityStamp ?? string.Empty)),
            isPersistent,
            previousSessionId,
            now);
        context.TwoFactorChallenges.Add(challenge);

        return new TwoFactorChallengeResponse
        {
            Flow = flow.Value,
            RequiredAction = challenge.RequiredAction,
            ExpiresAt = challenge.ExpiresAt
        };
    }

    /// <inheritdoc/>
    public async Task<bool> IsIssuedAsync(
        Guid challengeId,
        Guid memberId,
        string flow,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var challenge = await database.TwoFactorChallenges
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == challengeId && candidate.MemberId == memberId,
                cancellationToken);

        if (challenge is null)
            return false;

        if (!challenge.IsLive(timeProvider.GetUtcNow().UtcDateTime))
            throw new TwoFactorAuthenticationFailedException();

        return CryptographicOperations.FixedTimeEquals(
            cryptography.HashFlow(flow),
            challenge.FlowHash);
    }
}
