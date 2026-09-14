using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class TwoFactorChallengeIssuerIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Theory]
    [InlineData("valid")]
    [InlineData("missing")]
    [InlineData("otherMember")]
    [InlineData("otherFlow")]
    [InlineData("expired")]
    [InlineData("invalidated")]
    public async Task IsIssuedAsync_WhenConfirmingCommit_RequiresExactLiveProof(string scenario)
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
        var cryptography = scope.ServiceProvider.GetRequiredService<ITwoFactorCryptography>();
        var issuer = scope.ServiceProvider.GetRequiredService<ITwoFactorChallengeIssuer>();
        var flow = cryptography.CreateFlow();
        var now = clock.GetUtcNow().UtcDateTime;
        var challenge = TwoFactorChallenge.CreateSignIn(
            Guid.CreateVersion7(),
            memberId,
            flow.Hash,
            null,
            new byte[32],
            false,
            null,
            now);

        if (scenario == "invalidated")
            challenge.Invalidate(now);

        database.TwoFactorChallenges.Add(challenge);
        await database.SaveChangesAsync(cancellationToken);

        if (scenario == "expired")
            clock.Advance(TimeSpan.FromMinutes(5));

        var challengeId = scenario == "missing" ? Guid.CreateVersion7() : challenge.Id;
        var expectedMember = scenario == "otherMember" ? Guid.CreateVersion7() : memberId;
        var expectedFlow = scenario == "otherFlow" ? cryptography.CreateFlow().Value : flow.Value;
        bool? issued = null;

        // Act
        var exception = await Record.ExceptionAsync(async () =>
        {
            issued = await issuer.IsIssuedAsync(
                challengeId,
                expectedMember,
                expectedFlow,
                cancellationToken);
        });

        // Assert
        if (scenario is "expired" or "invalidated")
        {
            Assert.IsType<TwoFactorAuthenticationFailedException>(exception);
            Assert.Null(issued);
        }
        else
        {
            Assert.Null(exception);
            Assert.Equal(
                scenario == "valid",
                issued);
        }
    }

    [Fact]
    public async Task StageAsync_WhenEnabledCredentialIsMissing_FailsClosedWithoutStagingChallenge()
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
        var member = await database.Users.SingleAsync(
            candidate => candidate.Id == memberId,
            cancellationToken);
        member.TwoFactorEnabled = true;
        await database.SaveChangesAsync(cancellationToken);
        var issuer = scope.ServiceProvider.GetRequiredService<ITwoFactorChallengeIssuer>();
        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        // Act
        var exception = await Record.ExceptionAsync(() => issuer.StageAsync(
            member,
            Guid.CreateVersion7(),
            false,
            null,
            cancellationToken));

        // Assert
        Assert.IsType<TwoFactorUnavailableException>(exception);
        Assert.Empty(database.TwoFactorChallenges.Local);
        Assert.False(await database.TwoFactorChallenges.AnyAsync(cancellationToken));
    }
}
