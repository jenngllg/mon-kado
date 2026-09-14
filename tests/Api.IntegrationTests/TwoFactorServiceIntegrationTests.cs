using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Moq;

using System.Security.Cryptography;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class TwoFactorServiceIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task GetSetupAsync_WhenChallengeIsRemovedAfterLookup_RejectsWithoutStagingSecret()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var options = new DbContextOptionsBuilder<MonKadoDbContext>()
            .UseNpgsql(fixture.Container.GetConnectionString())
            .UseSnakeCaseNamingConvention()
            .Options;
        var interceptor = new TwoFactorLookupInterceptor(async token =>
        {
            await using var concurrent = new MonKadoDbContext(options);
            await concurrent.TwoFactorChallenges.ExecuteDeleteAsync(token);
        });
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services => services.AddDbContextPool<MonKadoDbContext>((
                _,
                databaseOptions) => databaseOptions.AddInterceptors(interceptor)));
        var memberId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var member = await database.Users.SingleAsync(
            candidate => candidate.Id == memberId,
            cancellationToken);
        var issuer = scope.ServiceProvider.GetRequiredService<ITwoFactorChallengeIssuer>();
        var challenge = await issuer.StageAsync(
            member,
            Guid.CreateVersion7(),
            false,
            null,
            cancellationToken);
        Assert.NotNull(challenge);
        await database.SaveChangesAsync(cancellationToken);
        var service = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();

        // Act
        var exception = await Record.ExceptionAsync(() => service.GetSetupAsync(
            challenge.Flow,
            cancellationToken));

        // Assert
        Assert.IsType<TwoFactorAuthenticationFailedException>(exception);
        Assert.False(await database.TwoFactorChallenges.AnyAsync(cancellationToken));
        Assert.False(await database.AuthenticationSessions.AnyAsync(cancellationToken));
        Assert.Null((await database.MemberTwoFactors
            .AsNoTracking()
            .SingleAsync(cancellationToken)).ProtectedSecret);
    }

    [Fact]
    public async Task RegenerateRecoveryCodesAsync_WhenCredentialIsMissing_RejectsWithoutCreatingCodes()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var callerMock = new Mock<ITwoFactorCallerProvider>(MockBehavior.Strict);
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services => services.AddScoped(_ => callerMock.Object));
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            memberId,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var member = await database.Users.SingleAsync(cancellationToken);
        var token = await database.AuthenticationAccessTokens.SingleAsync(cancellationToken);
        callerMock
            .Setup(provider => provider.GetCurrent())
            .Returns(new TwoFactorCaller
            {
                MemberId = memberId,
                AccessTokenId = token.Id
            });
        var cryptography = scope.ServiceProvider.GetRequiredService<ITwoFactorCryptography>();
        var flow = cryptography.CreateFlow();
        var now = scope.ServiceProvider
            .GetRequiredService<TimeProvider>()
            .GetUtcNow()
            .UtcDateTime;
        var challenge = TwoFactorChallenge.CreateSignIn(
            Guid.CreateVersion7(),
            memberId,
            flow.Hash,
            null,
            SHA256.HashData(Encoding.UTF8.GetBytes(member.SecurityStamp ?? string.Empty)),
            false,
            null,
            now);
        challenge.AuthorizeManagement(
            TwoFactorFlowPurpose.RegenerateRecoveryCodes,
            token.SessionId,
            now);
        database.MemberTwoFactors.Add(MemberTwoFactor.Create(memberId));
        database.TwoFactorChallenges.Add(challenge);
        await database.SaveChangesAsync(cancellationToken);
        var service = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();

        // Act
        var exception = await Record.ExceptionAsync(() => service.RegenerateRecoveryCodesAsync(
            flow.Value,
            cancellationToken));

        // Assert
        Assert.IsType<TwoFactorUnavailableException>(exception);
        Assert.False(await database.TwoFactorRecoveryCodes.AnyAsync(cancellationToken));
        callerMock.Verify(
            provider => provider.GetCurrent(),
            Times.Once);
        callerMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("missingMember")]
    [InlineData("unknownToken")]
    [InlineData("revokedSession")]
    [InlineData("nullStamp")]
    public async Task ReauthenticateAsync_WhenSessionIsRechecked_RequiresCurrentRegisteredProof(string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(TimeProvider.System.GetUtcNow());
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var memberId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            memberId,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var token = await database.AuthenticationAccessTokens.SingleAsync(cancellationToken);
        var factor = await database.MemberTwoFactors.SingleAsync(cancellationToken);
        var cryptography = scope.ServiceProvider.GetRequiredService<ITwoFactorCryptography>();
        var secret = cryptography.UnprotectSecret(
            memberId,
            factor.CredentialId.GetValueOrDefault(),
            Assert.IsType<string>(factor.ProtectedSecret));

        if (scenario == "missingMember")
            await database.Users.ExecuteDeleteAsync(cancellationToken);

        if (scenario == "revokedSession")
            await database.AuthenticationSessions.ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    session => session.RevokedAt,
                    clock.GetUtcNow().UtcDateTime),
                cancellationToken);

        if (scenario == "nullStamp")
            await database.Users.ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    member => member.SecurityStamp,
                    (string?)null),
                cancellationToken);

        clock.Advance(TimeSpan.FromSeconds(30));
        var service = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();
        TwoFactorChallengeResponse? grant = null;

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            grant = await service.ReauthenticateAsync(
                memberId,
                scenario == "unknownToken" ? Guid.CreateVersion7() : token.Id,
                TwoFactorFlowPurpose.ReplaceAuthenticator,
                TwoFactorTestData.CreateCurrentCode(
                    secret,
                    clock),
                null,
                cancellationToken);
        });

        // Assert
        if (scenario == "nullStamp")
        {
            Assert.Null(exception);
            Assert.NotNull(grant);
            Assert.Equal(
                TwoFactorRequiredAction.Replace,
                grant.RequiredAction);
        }
        else
        {

            if (scenario == "missingMember")
                Assert.IsType<TwoFactorAuthenticationFailedException>(exception);
            else
                Assert.IsType<TwoFactorAccessDeniedException>(exception);

            Assert.Null(grant);
        }
    }

    [Theory]
    [InlineData("missingFactor")]
    [InlineData("changedCredential")]
    [InlineData("missingExpectedCredential")]
    [InlineData("staleExpectedCredential")]
    [InlineData("changedStamp")]
    [InlineData("missingCode")]
    [InlineData("missingCredential")]
    [InlineData("missingCompletedCredential")]
    [InlineData("missingVerification")]
    [InlineData("nullStamp")]
    public async Task CompleteAsync_WhenStoredStateIsRechecked_FailsClosedUnlessProofRemainsValid(string scenario)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(TimeProvider.System.GetUtcNow());
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var member = await database.Users.SingleAsync(cancellationToken);

        if (scenario == "nullStamp")
            member.SecurityStamp = null;

        var cryptography = scope.ServiceProvider.GetRequiredService<ITwoFactorCryptography>();
        var secret = cryptography.CreateSecret();
        var credentialId = Guid.CreateVersion7();
        var factor = MemberTwoFactor.Create(memberId);

        if (scenario is not ("missingCredential" or "missingCompletedCredential" or "staleExpectedCredential"))
            factor.ConfirmAuthenticator(
                credentialId,
                cryptography.ProtectSecret(
                    memberId,
                    credentialId,
                    secret),
                0,
                clock.GetUtcNow().UtcDateTime);

        if (scenario != "missingFactor")
            database.MemberTwoFactors.Add(factor);

        var flow = cryptography.CreateFlow();
        var expectedCredential = factor.CredentialId;

        if (scenario is "changedCredential" or "staleExpectedCredential")
            expectedCredential = Guid.CreateVersion7();

        if (scenario == "missingExpectedCredential")
            expectedCredential = null;

        var challenge = TwoFactorChallenge.CreateSignIn(
            Guid.CreateVersion7(),
            memberId,
            flow.Hash,
            expectedCredential,
            SHA256.HashData(Encoding.UTF8.GetBytes(scenario == "changedStamp" ? "obsolete-stamp" : member.SecurityStamp ?? string.Empty)),
            false,
            null,
            clock.GetUtcNow().UtcDateTime);
        database.TwoFactorChallenges.Add(challenge);
        await database.SaveChangesAsync(cancellationToken);

        if (scenario is "missingCredential" or "missingCompletedCredential" or "missingVerification")
            await database.TwoFactorChallenges.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        candidate => candidate.RequiredAction,
                        scenario == "missingCredential" ? TwoFactorRequiredAction.Verify : TwoFactorRequiredAction.Complete)
                    .SetProperty(
                        candidate => candidate.VerifiedAt,
                        scenario == "missingCompletedCredential" ? clock.GetUtcNow().UtcDateTime : (DateTime?)null),
                cancellationToken);

        var service = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();
        TwoFactorCompletionResult? result = null;

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            result = await service.CompleteAsync(
                flow.Value,
                scenario is "missingCode" or "missingCompletedCredential" or "missingVerification" ? null : TwoFactorTestData.CreateCurrentCode(
                    secret,
                    clock),
                null,
                cancellationToken);
        });

        // Assert
        if (scenario == "nullStamp")
        {
            Assert.Null(exception);
            Assert.NotNull(result?.Tokens);
        }
        else
        {

            if (scenario is "missingFactor" or "missingCredential" or "missingCompletedCredential")
                Assert.IsType<TwoFactorUnavailableException>(exception);
            else
                Assert.IsType<TwoFactorAuthenticationFailedException>(exception);

            Assert.Null(result);
            Assert.False(await database.AuthenticationSessions.AnyAsync(cancellationToken));
        }
    }

    [Theory]
    [InlineData("missingMember")]
    [InlineData("unenrolledMember")]
    [InlineData("pendingEnrollment")]
    public async Task GetStatusAsync_WhenAccountHasNoConfirmedFactor_ReturnsOnlyStatusOrRejectsMissingAccount(string scenario)
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

        if (scenario == "pendingEnrollment")
        {
            database.MemberTwoFactors.Add(MemberTwoFactor.Create(memberId));
            await database.SaveChangesAsync(cancellationToken);
        }

        if (scenario == "missingMember")
            await database.Users.ExecuteDeleteAsync(cancellationToken);

        var service = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();
        TwoFactorStatusResponse? status = null;

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            status = await service.GetStatusAsync(
                memberId,
                cancellationToken);
        });

        // Assert
        if (scenario == "missingMember")
        {
            Assert.IsType<InvalidAuthenticationSessionException>(exception);
            Assert.Null(status);
        }
        else
        {
            Assert.Null(exception);
            Assert.NotNull(status);
            Assert.False(status.IsEnabled);
            Assert.Equal(
                0,
                status.RemainingRecoveryCodes);
        }
    }

    [Fact]
    public async Task GetSetupAsync_WhenProofWasNeverIssued_RejectsWithoutStagingSecurityMaterial()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();
        var cryptography = scope.ServiceProvider.GetRequiredService<ITwoFactorCryptography>();

        // Act
        var exception = await Record.ExceptionAsync(() => service.GetSetupAsync(
            cryptography.CreateFlow().Value,
            cancellationToken));

        // Assert
        Assert.IsType<TwoFactorAuthenticationFailedException>(exception);
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.False(await database.TwoFactorChallenges.AnyAsync(cancellationToken));
        Assert.False(await database.MemberTwoFactors.AnyAsync(cancellationToken));
    }

    [Fact]
    public async Task ReauthenticateAsync_WhenAccountHasNoAuthenticator_RejectsManagementGrant()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();

        // Act
        var exception = await Record.ExceptionAsync(() => service.ReauthenticateAsync(
            memberId,
            Guid.CreateVersion7(),
            TwoFactorFlowPurpose.ReplaceAuthenticator,
            "123456",
            null,
            cancellationToken));

        // Assert
        Assert.IsType<TwoFactorAccessDeniedException>(exception);
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.False(await database.TwoFactorChallenges.AnyAsync(cancellationToken));
    }
}
