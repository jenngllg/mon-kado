using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class TwoFactorSessionValidationIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Fact]
    public async Task GetCurrentAsync_WhenMemberIsPromoted_OldSingleFactorJwtImmediatelyFails()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var client = await AuthenticationTestData.CreateClientAsync(
            factory,
            memberId,
            cancellationToken);
        using var before = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            before.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = await manager.FindByIdAsync(memberId.ToString("D"))
            ?? throw new InvalidOperationException("Missing test member.");
        Assert.True((await manager.AddToRoleAsync(
            member,
            RoleNames.Admin)).Succeeded);

        // Act
        using var after = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        using var optional = await client.GetAsync(
            $"/api/v1/shared-wishlists/{Guid.CreateVersion7()}",
            cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            after.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            optional.StatusCode);
        Assert.False(after.Headers.Contains("Set-Cookie"));
        Assert.False(optional.Headers.Contains("Set-Cookie"));
    }

    [Theory]
    [InlineData("missingProof", false)]
    [InlineData("wrongVersion", false)]
    [InlineData("matchingVersion", true)]
    [InlineData("missingCredential", false)]
    public async Task GetCurrentAsync_WhenFormerAdministratorRetainsMfa_RequiresCurrentCredentialProof(
        string scenario,
        bool permitted)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await fixture.ResetDatabaseAsync(cancellationToken);
        await using var factory = new PostgreSqlApiFactory(fixture.Container.GetConnectionString());
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
        member.TwoFactorEnabled = true;
        var credentialId = Guid.CreateVersion7();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>()
            .GetUtcNow()
            .UtcDateTime;

        if (scenario != "missingCredential")
        {
            var factor = MemberTwoFactor.Create(memberId);
            factor.ConfirmAuthenticator(
                credentialId,
                "Opaque encrypted test credential, not read by JWT validation.",
                123,
                now);
            database.MemberTwoFactors.Add(factor);
        }

        if (scenario != "missingProof")
        {
            var session = await database.AuthenticationSessions.SingleAsync(cancellationToken);
            session.BindTwoFactor(
                scenario == "wrongVersion" ? Guid.CreateVersion7() : credentialId,
                now);
        }
        await database.SaveChangesAsync(cancellationToken);

        // Act
        using var response = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);

        // Assert
        Assert.Equal(
            permitted ? HttpStatusCode.OK : HttpStatusCode.Unauthorized,
            response.StatusCode);
    }
}
