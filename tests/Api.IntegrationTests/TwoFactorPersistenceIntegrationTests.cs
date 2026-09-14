using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class TwoFactorPersistenceIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task SaveChangesAsync_WhenSecondFactorStateIsPersisted_StoresProtectedSecretsAndHashesOnly()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var cryptography = scope.ServiceProvider.GetRequiredService<ITwoFactorCryptography>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var credentialId = Guid.CreateVersion7();
        var secret = cryptography.CreateSecret();
        var factor = MemberTwoFactor.Create(memberId);
        factor.ConfirmAuthenticator(
            credentialId,
            cryptography.ProtectSecret(
                memberId,
                credentialId,
                secret),
            123,
            now);
        var flow = cryptography.CreateFlow();
        var challenge = TwoFactorChallenge.CreateSignIn(
            Guid.CreateVersion7(),
            memberId,
            flow.Hash,
            credentialId,
            new byte[32],
            false,
            null,
            now);
        var code = cryptography.CreateRecoveryCodes()[0];
        database.MemberTwoFactors.Add(factor);
        database.TwoFactorChallenges.Add(challenge);
        database.TwoFactorRecoveryCodes.Add(TwoFactorRecoveryCode.Create(
            Guid.CreateVersion7(),
            memberId,
            credentialId,
            cryptography.HashRecoveryCode(code)));

        // Act
        await database.SaveChangesAsync(cancellationToken);
        database.ChangeTracker.Clear();
        var storedFactor = await database.MemberTwoFactors.SingleAsync(cancellationToken);
        var storedFlow = await database.TwoFactorChallenges.SingleAsync(cancellationToken);
        var storedCode = await database.TwoFactorRecoveryCodes.SingleAsync(cancellationToken);

        // Assert
        Assert.NotNull(storedFactor.ProtectedSecret);
        Assert.DoesNotContain(
            secret,
            storedFactor.ProtectedSecret,
            StringComparison.Ordinal);
        Assert.Equal(
            secret,
            cryptography.UnprotectSecret(
                memberId,
                credentialId,
                storedFactor.ProtectedSecret));
        Assert.Equal(
            flow.Hash,
            storedFlow.FlowHash);
        Assert.Equal(
            cryptography.HashRecoveryCode(code),
            storedCode.CodeHash);
        Assert.False(await database.UserTokens.AnyAsync(cancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RevokeAllForUserAsync_WhenSecurityEventOccurs_InvalidatesOutstandingProofAtomically(bool commit)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        var challenge = CreateEnrollment(
            memberId,
            now);
        challenge.StageAuthenticator(
            Guid.CreateVersion7(),
            "encrypted candidate",
            now);
        challenge.BindGoogleProof(
            Guid.CreateVersion7(),
            "encrypted identity");
        database.TwoFactorChallenges.Add(challenge);
        database.AuthenticationSessions.Add(AuthenticationSession.Create(
            Guid.CreateVersion7(),
            memberId,
            new byte[32],
            false,
            now,
            now.AddHours(8)));
        await database.SaveChangesAsync(cancellationToken);

        // Act
        await using (var transaction = await database.Database.BeginTransactionAsync(cancellationToken))
        {
            await scope.ServiceProvider.GetRequiredService<IAuthenticationSessionRepository>()
                .RevokeAllForUserAsync(
                    memberId,
                    now,
                    cancellationToken);

            if (commit)
                await transaction.CommitAsync(cancellationToken);
            else
                await transaction.RollbackAsync(cancellationToken);
        }
        database.ChangeTracker.Clear();
        var storedChallenge = await database.TwoFactorChallenges.SingleAsync(cancellationToken);
        var storedSession = await database.AuthenticationSessions.SingleAsync(cancellationToken);

        // Assert
        Assert.Equal(
            commit,
            storedChallenge.InvalidatedAt.HasValue);
        Assert.Equal(
            commit,
            storedSession.RevokedAt.HasValue);
        Assert.Equal(
            !commit,
            storedChallenge.IsLive(now));
        Assert.Equal(
            commit,
            storedChallenge.PendingProtectedSecret is null);
        Assert.Equal(
            commit,
            storedChallenge.PendingCredentialId is null);
        Assert.Equal(
            commit,
            storedChallenge.ProtectedGoogleContext is null);
    }

    [Fact]
    public async Task TryReserve_WhenTwoFlowsRaceUnderAccountLock_OnlyOneOwnsRecoveryCode()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        database.TwoFactorRecoveryCodes.Add(TwoFactorRecoveryCode.Create(
            Guid.CreateVersion7(),
            memberId,
            Guid.CreateVersion7(),
            new byte[32]));
        await database.SaveChangesAsync(cancellationToken);

        // Act
        var results = await Task.WhenAll(
            ReserveAsync(
                factory,
                memberId,
                now,
                cancellationToken),
            ReserveAsync(
                factory,
                memberId,
                now,
                cancellationToken));

        // Assert
        Assert.Single(
            results,
            result => result);
        Assert.Single(
            results,
            result => !result);
    }

    [Fact]
    public async Task DeleteExpiredSessionsAsync_WhenChallengesReachRetentionBoundary_DeletesOnlyExpiredProofs()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        database.TwoFactorChallenges.Add(CreateEnrollment(
            memberId,
            now.AddMinutes(-66)));
        database.TwoFactorChallenges.Add(CreateEnrollment(
            memberId,
            now.AddMinutes(-65)));
        var retained = CreateEnrollment(
            memberId,
            now.AddMinutes(-64));
        database.TwoFactorChallenges.Add(retained);
        await database.SaveChangesAsync(cancellationToken);
        var cleanup = scope.ServiceProvider.GetRequiredService<IExpiredAuthenticationSessionCleanup>();

        // Act
        await cleanup.DeleteExpiredSessionsAsync(
            now,
            1,
            cancellationToken);
        await cleanup.DeleteExpiredSessionsAsync(
            now,
            1,
            cancellationToken);

        // Assert
        var remaining = await database.TwoFactorChallenges.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            retained.Id,
            remaining.Id);
    }

    [Fact]
    public async Task DeleteMember_WhenSecondFactorDataExists_CascadesAllSecurityMaterial()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        database.MemberTwoFactors.Add(MemberTwoFactor.Create(memberId));
        database.TwoFactorChallenges.Add(CreateEnrollment(
            memberId,
            now));
        database.TwoFactorRecoveryCodes.Add(TwoFactorRecoveryCode.Create(
            Guid.CreateVersion7(),
            memberId,
            Guid.CreateVersion7(),
            new byte[32]));
        await database.SaveChangesAsync(cancellationToken);

        // Act
        await database.Users.Where(member => member.Id == memberId).ExecuteDeleteAsync(cancellationToken);

        // Assert
        Assert.False(await database.MemberTwoFactors.AnyAsync(cancellationToken));
        Assert.False(await database.TwoFactorChallenges.AnyAsync(cancellationToken));
        Assert.False(await database.TwoFactorRecoveryCodes.AnyAsync(cancellationToken));
    }

    private static TwoFactorChallenge CreateEnrollment(
        Guid memberId,
        DateTime now)
    {

        return TwoFactorChallenge.CreateSignIn(
            Guid.CreateVersion7(),
            memberId,
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32),
            null,
            new byte[32],
            false,
            null,
            now);
    }

    private static async Task<bool> ReserveAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        _ = await database.Users
            .FromSqlInterpolated($"SELECT *, xmin FROM public.users WHERE id = {memberId} FOR UPDATE")
            .SingleAsync(cancellationToken);
        var code = await database.TwoFactorRecoveryCodes.SingleAsync(cancellationToken);
        var reserved = code.TryReserve(
            Guid.CreateVersion7(),
            now.AddMinutes(5),
            now);
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return reserved;
    }
}
