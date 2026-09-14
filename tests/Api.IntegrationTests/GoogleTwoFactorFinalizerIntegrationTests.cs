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
public class GoogleTwoFactorFinalizerIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Theory]
    [InlineData("otherFlow")]
    [InlineData("otherPersistence")]
    [InlineData("otherBrowser")]
    [InlineData("otherSubject")]
    [InlineData("subjectAlreadyOwned")]
    [InlineData("lockedAccount")]
    [InlineData("unconfirmedPasswordLink")]
    [InlineData("nonAuthoritativeIdentity")]
    [InlineData("confirmedWorkspaceAccount")]
    [InlineData("linkedEmailConfirmation")]
    [InlineData("automaticEmailConfirmation")]
    public async Task FinalizeAsync_WhenProtectedProofIsRechecked_EnforcesCurrentBindingAndAssociationRules(string scenario)
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
        var otherMemberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var member = await database.Users.SingleAsync(
            candidate => candidate.Id == memberId,
            cancellationToken);
        var confirmsEmail = scenario is "linkedEmailConfirmation" or "automaticEmailConfirmation";
        var email = confirmsEmail ? "verified@gmail.com" : "verified@example.test";
        member.Email = email;
        member.NormalizedEmail = email.ToUpperInvariant();
        member.EmailConfirmed = !confirmsEmail && scenario != "unconfirmedPasswordLink";
        member.LockoutEnabled = true;
        member.LockoutEnd = scenario == "lockedAccount" ? clock.GetUtcNow().AddMinutes(15) : null;
        const string subject = "verified-google-subject";

        if (scenario is "otherSubject" or "subjectAlreadyOwned" or "linkedEmailConfirmation")
        {
            database.UserLogins.Add(new IdentityUserLogin<Guid>
            {
                LoginProvider = ExternalLoginProviders.Google,
                ProviderDisplayName = ExternalLoginProviders.Google,
                ProviderKey = scenario == "otherSubject" ? "different-subject" : subject,
                UserId = scenario == "subjectAlreadyOwned" ? otherMemberId : memberId
            });
        }

        var flowId = Guid.CreateVersion7();
        var cryptography = scope.ServiceProvider.GetRequiredService<ITwoFactorCryptography>();
        var challenge = TwoFactorChallenge.CreateSignIn(
            Guid.CreateVersion7(),
            memberId,
            cryptography.CreateFlow().Hash,
            null,
            new byte[32],
            false,
            null,
            clock.GetUtcNow().UtcDateTime);
        var proof = new GoogleTwoFactorProof(
            new GoogleAuthenticationContext(
                new GoogleIdentity(
                    subject,
                    email,
                    true,
                    scenario == "confirmedWorkspaceAccount" ? "example.test" : null,
                    "Verified name"),
                scenario == "otherPersistence",
                "/login/google-return",
                scenario == "otherFlow" ? Guid.CreateVersion7() : flowId,
                memberId,
                scenario == "otherBrowser" ? Guid.CreateVersion7() : null),
            scenario is not ("nonAuthoritativeIdentity" or "confirmedWorkspaceAccount" or "automaticEmailConfirmation"));
        var protector = scope.ServiceProvider.GetRequiredService<IGoogleTwoFactorProofProtector>();
        challenge.BindGoogleProof(
            flowId,
            protector.Protect(
                memberId,
                challenge.Id,
                proof));
        database.TwoFactorChallenges.Add(challenge);
        await database.SaveChangesAsync(cancellationToken);
        var finalizer = scope.ServiceProvider.GetRequiredService<IGoogleTwoFactorFinalizer>();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Act
        var exception = await Record.ExceptionAsync(() => finalizer.FinalizeAsync(
            member,
            challenge,
            cancellationToken));

        // Assert
        if (confirmsEmail)
        {
            Assert.Null(exception);
            Assert.True(member.EmailConfirmed);
            Assert.Equal(
                "Verified name",
                member.DisplayName);
            await database.SaveChangesAsync(cancellationToken);
            Assert.Equal(
                1,
                await database.UserLogins.CountAsync(
                    login => login.UserId == memberId && login.ProviderKey == subject,
                    cancellationToken));
        }
        else if (scenario is "otherSubject" or "subjectAlreadyOwned")
            Assert.IsType<GoogleAccountLinkConflictException>(exception);
        else
            Assert.IsType<GoogleAuthenticationFailedException>(exception);

        Assert.False(await database.AuthenticationSessions.AnyAsync(cancellationToken));
        Assert.False(await database.AuthenticationAccessTokens.AnyAsync(cancellationToken));
    }
}
