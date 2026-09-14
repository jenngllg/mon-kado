using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using System.Net.Http.Headers;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

/// <summary>Creates committed credentials for tests that do not exercise the login endpoint.</summary>
public static class AuthenticationTestData
{
    /// <summary>Seeds an independently revocable session and its exact access token.</summary>
    /// <param name="factory">The API factory.</param>
    /// <param name="memberId">The intended token subject.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A client carrying registered credentials, or an unknown subject for negative tests.</returns>
    public static async Task<HttpClient> CreateClientAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        // Business-clock fixtures can precede the wall clock used by JWT cryptographic validation.
        var issuanceTime = clock.GetUtcNow() > TimeProvider.System.GetUtcNow() ? clock.GetUtcNow() : TimeProvider.System.GetUtcNow();
        var issuer = new JwtAccessTokenService(
            scope.ServiceProvider.GetRequiredService<IOptions<JwtOptions>>(),
            new FixedTimeProvider(issuanceTime));
        var token = issuer.Create(memberId);

        if (await database.Users.AnyAsync(
            member => member.Id == memberId,
            cancellationToken))
        {
            var sessionId = Guid.CreateVersion7();
            var session = AuthenticationSession.Create(
                sessionId,
                memberId,
                new byte[32],
                false,
                clock
                    .GetUtcNow()
                    .UtcDateTime,
                clock
                    .GetUtcNow()
                    .UtcDateTime.AddHours(8));
            await BindCompletedTwoFactorAsync(
                scope.ServiceProvider,
                database,
                session,
                clock,
                cancellationToken);
            database.AuthenticationSessions.Add(session);
            database.AuthenticationAccessTokens.Add(new AuthenticationAccessToken
            {
                Id = token.Id,
                SessionId = sessionId,
                IssuedAt = token.IssuedAt,
                ExpiresAt = token.ExpiresAt
            });
            await database.SaveChangesAsync(cancellationToken);
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.Value);

        return client;
    }

    /// <summary>Seeds completed MFA only for business tests that deliberately bypass the real sign-in protocol.</summary>
    /// <param name="services">The test scope.</param>
    /// <param name="database">The shared test context.</param>
    /// <param name="session">The business-test session.</param>
    /// <param name="clock">The business clock.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>A task completed when any required MFA proof has been staged.</returns>
    private static async Task BindCompletedTwoFactorAsync(
        IServiceProvider services,
        MonKadoDbContext database,
        AuthenticationSession session,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var factor = await database.MemberTwoFactors.SingleOrDefaultAsync(
            candidate => candidate.MemberId == session.UserId,
            cancellationToken);
        var administrator = await services.GetRequiredService<IAdministratorAccessService>().GetAccessAsync(
            session.UserId,
            cancellationToken);

        if (factor?.CredentialId is null && administrator != AdministratorAccess.Granted)
            return;

        if (factor?.CredentialId is null)
        {
            var cryptography = services.GetRequiredService<ITwoFactorCryptography>();
            var credentialId = Guid.CreateVersion7();
            var secret = cryptography.CreateSecret();

            if (factor is null)
            {
                factor = MemberTwoFactor.Create(session.UserId);
                database.MemberTwoFactors.Add(factor);
            }

            factor.ConfirmAuthenticator(
                credentialId,
                cryptography.ProtectSecret(
                    session.UserId,
                    credentialId,
                    secret),
                clock.GetUtcNow().ToUnixTimeSeconds() / 30,
                clock.GetUtcNow().UtcDateTime);
            var member = await database.Users.SingleAsync(
                user => user.Id == session.UserId,
                cancellationToken);
            member.TwoFactorEnabled = true;
        }

        session.BindTwoFactor(
            factor.CredentialId ?? throw new InvalidOperationException("The test authenticator was not initialized."),
            clock.GetUtcNow().UtcDateTime);
    }
}
