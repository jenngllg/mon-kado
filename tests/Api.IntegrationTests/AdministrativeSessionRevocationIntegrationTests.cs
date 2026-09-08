using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class AdministrativeSessionRevocationIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Password = "A long test password for MK-809";
    [Fact]
    public async Task ExecuteAsync_WhenSeveralDevicesExist_RevokesAllTheirTokensAndAllowsNewLogin()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(
            [],
            cancellationToken);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await CreatePasswordMemberAsync(
            factory,
            cancellationToken);
        using var administrator = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            cancellationToken);
        var first = await LoginAsync(
                factory,
                memberId,
                cancellationToken);
        var second = await LoginAsync(
                factory,
                memberId,
                cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();
        var rotated = await sessions.RefreshAsync(
            first.RefreshToken,
            cancellationToken);
        Assert.NotNull(rotated);
        using var firstClient = CreateClient(
            factory,
            first.AccessToken);
        using var secondClient = CreateClient(
            factory,
            second.AccessToken);
        using var rotatedClient = CreateClient(
            factory,
            rotated.AccessToken);
        using var before = await firstClient.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            before.StatusCode);

        // Act
        using var response = await administrator.PostAsJsonAsync(
            GetRoute(memberId),
                new
                {
                    confirmedMemberId = memberId,
                    requestReference = "  SUPPORT-809  "
                },
            cancellationToken);
        using var firstAfter = await firstClient.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        using var secondAfter = await secondClient.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        using var rotatedAfter = await rotatedClient.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        var refreshAfter = await sessions.RefreshAsync(
            rotated.RefreshToken,
            cancellationToken);
        var reconnected = await LoginAsync(
                factory,
                memberId,
                cancellationToken);
        using var reconnectedClient = CreateClient(
            factory,
            reconnected.AccessToken);
        using var current = await reconnectedClient.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        using var journal = await administrator.GetAsync(
            $"/api/v1/admin/audit-events?action=memberSessionsRevoked&memberId={memberId}&requestReference=SUPPORT-809",
            cancellationToken);
        var audit = await journal.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            firstAfter.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            secondAfter.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            rotatedAfter.StatusCode);
        Assert.Null(refreshAfter);
        Assert.Equal(
            HttpStatusCode.OK,
            current.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            journal.StatusCode);
        var entry = Assert.Single(audit
            .GetProperty("items")
            .EnumerateArray());
        Assert.Equal(
            memberId,
            entry
                .GetProperty("memberId")
                .GetGuid());
        Assert.Equal(
            administratorId,
            entry
                .GetProperty("administratorId")
                .GetGuid());
        Assert.Equal(
            "SUPPORT-809",
            entry
                .GetProperty("requestReference")
                .GetString());
        Assert.Equal(
            JsonValueKind.Null,
            entry
                .GetProperty("wishlistId")
                .ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            entry
                .GetProperty("reason")
                .ValueKind);
        Assert.Equal(
            JsonValueKind.Null,
            entry
                .GetProperty("exportId")
                .ValueKind);
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var metadata = await database.AuthenticationAccessTokens
            .AsNoTracking()
            .SingleAsync(
            token => token.Id == rotated.AccessToken.Id,
            cancellationToken);
        Assert.Equal(
            rotated.AccessToken.IssuedAt,
            metadata.IssuedAt);
        Assert.Equal(
            rotated.AccessToken.ExpiresAt,
            metadata.ExpiresAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_WhenTargetHasNoSessions_RecordsEveryRequestWithoutChangingAccount(bool confirmed)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(
            [],
            cancellationToken);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await database.Users
            .Where(member => member.Id == memberId)
            .ExecuteUpdateAsync(
            setters => setters.SetProperty(
                member => member.EmailConfirmed,
                confirmed),
            cancellationToken);
        var service = scope.ServiceProvider.GetRequiredService<IAdministrativeSessionRevocationService>();

        // Act
        var first = await service.ExecuteAsync(
            administratorId,
            memberId,
            "SUPPORT-809",
            cancellationToken);
        var second = await service.ExecuteAsync(
            administratorId,
            memberId,
            "SUPPORT-809",
            cancellationToken);

        // Assert
        Assert.NotEqual(
            first,
            second);
        Assert.Equal(
            2,
            await database.AdministrativeSessionRevocationEvents.CountAsync(cancellationToken));
        Assert.Equal(
            confirmed,
            await database.Users
                .Where(member => member.Id == memberId)
                .Select(member => member.EmailConfirmed)
                .SingleAsync(cancellationToken));
        Assert.Empty(await database.AuthenticationSessions.ToArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData("self", typeof(AccountSelfSessionRevocationNotAllowedException))]
    [InlineData("target", typeof(AccountSessionRevocationTargetNotFoundException))]
    [InlineData("actor", typeof(InvalidAuthenticationSessionException))]
    [InlineData("role", typeof(AdministratorAccessDeniedException))]
    public async Task ExecuteAsync_WhenAccessChanges_RechecksLockedAccountsAndDoesNotAudit(
        string scenario,
        Type expected)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var factory = await CreateFactoryAsync(
            [],
            cancellationToken);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        if (scenario is "role")
            await database.UserRoles
                .Where(role => role.UserId == administratorId)
                .ExecuteDeleteAsync(cancellationToken);

        if (scenario is "actor")
            await database.Users
                .Where(member => member.Id == administratorId)
                .ExecuteDeleteAsync(cancellationToken);
        var target = scenario switch
        {
            "self" => administratorId,
            "target" => Guid.CreateVersion7(),
            _ => memberId
        };
        var service = scope.ServiceProvider.GetRequiredService<IAdministrativeSessionRevocationService>();

        // Act
        var exception = await Record.ExceptionAsync(() => service.ExecuteAsync(
            administratorId,
            target,
            "SUPPORT-809",
            cancellationToken));

        // Assert
        Assert.IsType(
            expected,
            exception);
        Assert.Empty(await database.AdministrativeSessionRevocationEvents.ToArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_WhenCommitAcknowledgementFails_ConfirmsOnlyTheExactDurableOperation(bool committed)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var interceptor = new WishlistModerationCommitInterceptor();
        await using var factory = await CreateFactoryAsync(
            [interceptor],
            cancellationToken);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var administrator = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            cancellationToken);
        using var memberClient = await AuthenticationTestData.CreateClientAsync(
            factory,
            memberId,
            cancellationToken);

        if (committed)
            interceptor.Arm();
        else
            interceptor.ArmBeforeCommit();

        // Act
        using var response = await administrator.PostAsJsonAsync(
            GetRoute(memberId),
                new
                {
                    confirmedMemberId = memberId,
                    requestReference = "SUPPORT-809"
                },
            cancellationToken);
        using var current = await memberClient.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);

        // Assert
        Assert.Equal(
            committed ? HttpStatusCode.NoContent : HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        Assert.Equal(
            committed ? HttpStatusCode.Unauthorized : HttpStatusCode.OK,
            current.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            committed ? 1 : 0,
            await database.AdministrativeSessionRevocationEvents.CountAsync(cancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_WhenCommitCannotBeVerified_ReturnsUnavailableWithoutReplaying()
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var failure = new AccountDeletionVerificationFailure();
        var interceptor = new AccountDeletionLostCommitInterceptor(failure);
        await using var factory = await CreateFactoryAsync(
            [interceptor,
            failure],
            cancellationToken);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        using var administrator = await AuthenticationTestData.CreateClientAsync(
            factory,
            administratorId,
            cancellationToken);
        interceptor.Armed = true;

        // Act
        using var response = await administrator.PostAsJsonAsync(
            GetRoute(memberId),
                new
                {
                    confirmedMemberId = memberId,
                    requestReference = "SUPPORT-809"
                },
            cancellationToken);
        failure.Unavailable = false;

        // Assert
        Assert.Equal(
            HttpStatusCode.ServiceUnavailable,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Single(await database.AdministrativeSessionRevocationEvents.ToArrayAsync(cancellationToken));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_WhenLoginRaces_SerializesWithTheAccountLock(bool revocationFirst)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var barrier = new AccountDeletionCommitBarrier();
        await using var factory = await CreateFactoryAsync(
            [barrier],
            cancellationToken);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await CreatePasswordMemberAsync(
            factory,
            cancellationToken);
        var original = await LoginAsync(
                factory,
                memberId,
                cancellationToken);
        await using var revocationScope = factory.Services.CreateAsyncScope();
        var revocations = revocationScope.ServiceProvider.GetRequiredService<IAdministrativeSessionRevocationService>();
        barrier.Arm();
        Task<Guid> revocation;
        Task<AccountSessionTokens> login;

        // Act

        if (revocationFirst)
        {
            revocation = revocations.ExecuteAsync(
                administratorId,
                memberId,
                "SUPPORT-809",
                cancellationToken);
            await barrier.WaitUntilEnteredAsync(cancellationToken);
            login = LoginAsync(
                factory,
                memberId,
                cancellationToken);
        }
        else
        {
            login = LoginAsync(
                factory,
                memberId,
                cancellationToken);
            await barrier.WaitUntilEnteredAsync(cancellationToken);
            revocation = revocations.ExecuteAsync(
                administratorId,
                memberId,
                "SUPPORT-809",
                cancellationToken);
        }

        barrier.Release();
        await Task.WhenAll(
            revocation,
            login);
        using var newClient = CreateClient(
            factory,
            (await login).AccessToken);
        using var current = await newClient.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);
        using var originalClient = CreateClient(
            factory,
            original.AccessToken);
        using var previous = await originalClient.GetAsync(
            "/api/v1/auth/sessions/current",
            cancellationToken);

        // Assert
        Assert.Equal(
            revocationFirst ? HttpStatusCode.OK : HttpStatusCode.Unauthorized,
            current.StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            previous.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteAsync_WhenRefreshRaces_NeverReturnsUsableCredentialsFromTheRevokedSession(bool revocationFirst)
    {
        // Arrange
        var cancellationToken = TestContext.Current.CancellationToken;
        var barrier = new AccountDeletionCommitBarrier();
        await using var factory = await CreateFactoryAsync(
            [barrier],
            cancellationToken);
        var administratorId = await ReportedWishlistTestData.CreateAdministratorAsync(
            factory,
            cancellationToken);
        var memberId = await CreatePasswordMemberAsync(
            factory,
            cancellationToken);
        var original = await LoginAsync(
                factory,
                memberId,
                cancellationToken);
        await using var revocationScope = factory.Services.CreateAsyncScope();
        await using var refreshScope = factory.Services.CreateAsyncScope();
        var revocations = revocationScope.ServiceProvider.GetRequiredService<IAdministrativeSessionRevocationService>();
        var sessions = refreshScope.ServiceProvider.GetRequiredService<IAccountSessionService>();
        barrier.Arm();
        Task<Guid> revocation;
        Task<AccountSessionTokens?> refresh;

        // Act

        if (revocationFirst)
        {
            revocation = revocations.ExecuteAsync(
                administratorId,
                memberId,
                "SUPPORT-809",
                cancellationToken);
            await barrier.WaitUntilEnteredAsync(cancellationToken);
            refresh = sessions.RefreshAsync(
                original.RefreshToken,
                cancellationToken);
        }
        else
        {
            refresh = sessions.RefreshAsync(
                original.RefreshToken,
                cancellationToken);
            await barrier.WaitUntilEnteredAsync(cancellationToken);
            revocation = revocations.ExecuteAsync(
                administratorId,
                memberId,
                "SUPPORT-809",
                cancellationToken);
        }

        barrier.Release();
        await Task.WhenAll(
            revocation,
            refresh);
        var result = await refresh;

        // Assert

        if (revocationFirst)
            Assert.Null(result);
        else
        {
            Assert.NotNull(result);
            using var client = CreateClient(
                factory,
                result.AccessToken);
            using var current = await client.GetAsync(
                "/api/v1/auth/sessions/current",
                cancellationToken);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                current.StatusCode);
        }
    }

    private async Task<PostgreSqlApiFactory> CreateFactoryAsync(
        IInterceptor[] interceptors,
        CancellationToken cancellationToken)
    {
        await fixture.ResetDatabaseAsync(cancellationToken);

        return new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: services => services.ConfigureDbContext<MonKadoDbContext>((
                _,
                options) => options.AddInterceptors(interceptors)));
    }

    private static async Task<Guid> CreatePasswordMemberAsync(
        PostgreSqlApiFactory factory,
        CancellationToken cancellationToken)
    {
        var memberId = await ReportedWishlistTestData.CreateOwnerAsync(
            factory,
            cancellationToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var member = await manager.FindByIdAsync(memberId.ToString());
        Assert.NotNull(member);
        Assert.True((await manager.AddPasswordAsync(
            member,
            Password)).Succeeded);

        return memberId;
    }

    private static async Task<AccountSessionTokens> LoginAsync(
        PostgreSqlApiFactory factory,
        Guid memberId,
        CancellationToken cancellationToken)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider
            .GetRequiredService<IAccountSessionService>()
            .LoginAsync(
            $"{memberId:N}@example.test",
            Password,
            false,
            null,
            cancellationToken);
        Assert.NotNull(result.Tokens);

        return result.Tokens;
    }

    private static HttpClient CreateClient(
        PostgreSqlApiFactory factory,
        AccessToken token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            token.Value);

        return client;
    }

    private static string GetRoute(Guid memberId) => $"/api/v1/admin/members/{memberId}/session-revocations";
}
