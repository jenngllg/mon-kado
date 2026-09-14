using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class GoogleTwoFactorIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Password = "An explicit secure test password";
    private const string Subject = "administrator-google-subject";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteAsync_WhenGoogleAssociationWinsAfterLookup_ReturnsConflictAndRollsBackSession(bool subjectBelongsToAnotherMember)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var clock = new MutableTimeProvider(TimeProvider.System.GetUtcNow());
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock,
            configureServices: services => services.AddScoped<IGoogleAccountRepository, MissGoogleLoginLookupRepository>());
        var memberId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var otherMemberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = await database.Users.SingleAsync(
            candidate => candidate.Id == memberId,
            cancellationToken);
        member.Email = "administrator@gmail.com";
        member.UserName = member.Email;
        Assert.True((await manager.UpdateAsync(member)).Succeeded);
        var google = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();
        var firstFactor = await google.CompleteAsync(
            new GoogleAuthenticationContext(
                new GoogleIdentity(
                    Subject,
                    member.Email,
                    true,
                    null,
                    "Administrator"),
                false,
                "/login/google-return",
                Guid.CreateVersion7(),
                memberId,
                null),
            cancellationToken);
        var challenge = firstFactor.Challenge;
        Assert.NotNull(challenge);
        var service = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();
        var setup = await service.GetSetupAsync(
            challenge.Flow,
            cancellationToken);
        await service.ConfirmSetupAsync(
            challenge.Flow,
            TwoFactorTestData.CreateCurrentCode(
                setup.ManualKey,
                clock),
            cancellationToken);
        database.UserLogins.Add(new IdentityUserLogin<Guid>
        {
            LoginProvider = ExternalLoginProviders.Google,
            ProviderDisplayName = ExternalLoginProviders.Google,
            ProviderKey = subjectBelongsToAnotherMember ? Subject : "another-google-subject",
            UserId = subjectBelongsToAnotherMember ? otherMemberId : memberId
        });
        await database.SaveChangesAsync(cancellationToken);

        // Act
        var exception = await Record.ExceptionAsync(() => service.CompleteAsync(
            challenge.Flow,
            null,
            null,
            cancellationToken));

        // Assert
        Assert.IsType<GoogleAccountLinkConflictException>(exception);
        database.ChangeTracker.Clear();
        Assert.False(await database.AuthenticationSessions.AnyAsync(cancellationToken));
        Assert.False(await database.AuthenticationAccessTokens.AnyAsync(cancellationToken));
        Assert.Null((await database.TwoFactorChallenges.SingleAsync(cancellationToken)).ConsumedAt);
        Assert.Equal(
            1,
            await database.UserLogins.CountAsync(cancellationToken));
    }

    [Theory]
    [InlineData(
        "linked",
        false)]
    [InlineData(
        "automatic",
        false)]
    [InlineData(
        "password",
        false)]
    [InlineData(
        "linked",
        true)]
    [InlineData(
        "automatic",
        true)]
    [InlineData(
        "password",
        true)]
    public async Task CompleteAsync_WhenAdministratorRequiresEnrollment_DefersAssociationAndSessionUntilSecondFactor(
        string scenario,
        bool loseCommitAcknowledgement)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        var interceptor = new AmbiguousCommitInterceptor();
        var clock = new MutableTimeProvider(DateTimeOffset.FromUnixTimeSeconds(TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()));
        await using var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            clock,
            configureServices: services => services.AddDbContextPool<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptor)));
        var memberId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = await database.Users.SingleAsync(
            user => user.Id == memberId,
            cancellationToken);
        var email = scenario == "password" ? "administrator@example.test" : "administrator@gmail.com";
        member.Email = email;
        member.UserName = email;
        Assert.True((await manager.UpdateAsync(member)).Succeeded);
        Assert.True((await manager.AddPasswordAsync(
            member,
            Password)).Succeeded);

        if (scenario == "linked")
        {
            Assert.True((await manager.AddLoginAsync(
                member,
                new UserLoginInfo(
                    ExternalLoginProviders.Google,
                    Subject,
                    ExternalLoginProviders.Google))).Succeeded);
        }

        var originalStamp = member.SecurityStamp;
        var proof = new GoogleAuthenticationContext(
            new GoogleIdentity(
                Subject,
                email,
                true,
                null,
                "Google administrator"),
            true,
            "/login/google-return",
            Guid.CreateVersion7(),
            memberId,
            null);
        var google = scope.ServiceProvider.GetRequiredService<IGoogleAccountSessionService>();

        if (loseCommitAcknowledgement)
            interceptor.Arm();

        // Act
        TwoFactorChallengeResponse? challenge;

        if (scenario == "password")
        {
            var result = await google.LinkAsync(
                proof,
                Password,
                cancellationToken);
            Assert.Equal(
                GoogleAccountLinkOutcome.TwoFactorRequired,
                result.Outcome);
            Assert.Null(result.Tokens);
            challenge = result.Challenge;
        }
        else
        {
            var result = await google.CompleteAsync(
                proof,
                cancellationToken);
            Assert.Equal(
                GoogleAuthenticationOutcome.TwoFactorRequired,
                result.Outcome);
            Assert.Null(result.Session);
            Assert.Null(result.AccessToken);
            challenge = result.Challenge;
        }

        // Assert
        Assert.NotNull(challenge);
        Assert.Equal(
            TwoFactorRequiredAction.Enroll,
            challenge.RequiredAction);
        Assert.False(await database.AuthenticationSessions.AnyAsync(cancellationToken));
        Assert.False(await database.AuthenticationAccessTokens.AnyAsync(cancellationToken));
        Assert.Equal(
            scenario == "linked",
            await database.UserLogins.AnyAsync(cancellationToken));
        var persistedMember = await database.Users.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            originalStamp,
            persistedMember.SecurityStamp);
        var pending = await database.TwoFactorChallenges.AsNoTracking().SingleAsync(cancellationToken);
        Assert.NotNull(pending.ProtectedGoogleContext);
        Assert.DoesNotContain(
            Subject,
            pending.ProtectedGoogleContext);
        Assert.DoesNotContain(
            email,
            pending.ProtectedGoogleContext);

        await Assert.ThrowsAsync<GoogleAuthenticationFailedException>(() => google.CompleteAsync(
            proof,
            cancellationToken));

        var factors = scope.ServiceProvider.GetRequiredService<ITwoFactorService>();
        var setup = await factors.GetSetupAsync(
            challenge.Flow,
            cancellationToken);
        var codes = await factors.ConfirmSetupAsync(
            challenge.Flow,
            TwoFactorTestData.CreateCurrentCode(
                setup.ManualKey,
                clock),
            cancellationToken);
        Assert.Equal(
            10,
            codes.RecoveryCodes.Count());
        Assert.False(await database.AuthenticationSessions.AnyAsync(cancellationToken));
        Assert.Equal(
            scenario == "linked",
            await database.UserLogins.AnyAsync(cancellationToken));

        var completed = await factors.CompleteAsync(
            challenge.Flow,
            null,
            null,
            cancellationToken);
        Assert.NotNull(completed.Tokens);
        Assert.Null(completed.Challenge);
        var session = await database.AuthenticationSessions.AsNoTracking().SingleAsync(cancellationToken);
        Assert.NotNull(session.TwoFactorCredentialId);
        Assert.NotNull(session.TwoFactorVerifiedAt);
        var login = await database.UserLogins.AsNoTracking().SingleAsync(cancellationToken);
        Assert.Equal(
            Subject,
            login.ProviderKey);
        Assert.Equal(
            memberId,
            login.UserId);
        pending = await database.TwoFactorChallenges.AsNoTracking().SingleAsync(cancellationToken);
        Assert.NotNull(pending.ConsumedAt);
        Assert.Null(pending.ProtectedGoogleContext);
        await Assert.ThrowsAsync<TwoFactorAuthenticationFailedException>(() => factors.CompleteAsync(
            challenge.Flow,
            null,
            null,
            cancellationToken));
    }
}
