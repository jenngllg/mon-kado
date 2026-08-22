using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class LogoutIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Password = "  a long secure password  ";
    private static readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task LogoutAsync_WhenSessionIsValid_RevokesRefreshAndLeavesAccessTokenValid()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        var email = "logout-valid@example.fr";
        await CreateUserAsync(
            factory,
            email);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            email);
        var accessToken = await ReadAccessTokenAsync(loginResponse);
        var refreshCookie = GetRefreshCookiePair(loginResponse);

        // Act
        using var response = await LogoutAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        AssertTimestampClose(
            _now.UtcDateTime,
            session.RevokedAt);

        var csrf = await GetCsrfExchangeAsync(factory);
        using (csrf.Client)
        {
            using var refreshResponse = await RefreshAsync(
                csrf.Client,
                csrf.Token,
                $"{csrf.Cookie}; {refreshCookie}");
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                refreshResponse.StatusCode);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            accessToken.AccessToken);
        using var currentResponse = await client.GetAsync(
            "/api/v1/auth/sessions/current",
            TestContext.Current.CancellationToken);
        Assert.Equal(
            HttpStatusCode.OK,
            currentResponse.StatusCode);
    }

    [Fact]
    public async Task LogoutAsync_WhenSeveralDevicesAreConnected_RevokesOnlyCurrentDevice()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        var email = "logout-devices@example.fr";
        await CreateUserAsync(
            factory,
            email);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        using var firstLoginResponse = await LoginAsync(
            firstClient,
            email);
        using var secondLoginResponse = await LoginAsync(
            secondClient,
            email);
        var firstSessionId = GetSessionId(firstLoginResponse);
        var secondSessionId = GetSessionId(secondLoginResponse);

        // Act
        using var logoutResponse = await LogoutAsync(firstClient);
        using var secondRefreshResponse = await RefreshAsync(secondClient);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            logoutResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.OK,
            secondRefreshResponse.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var sessions = await context.AuthenticationSessions
            .AsNoTracking()
            .ToDictionaryAsync(
                session => session.Id,
                TestContext.Current.CancellationToken);
        Assert.NotNull(sessions[firstSessionId].RevokedAt);
        Assert.Null(sessions[secondSessionId].RevokedAt);
    }

    [Fact]
    public async Task LogoutAsync_WhenSessionDoesNotExist_DoesNotPersistChanges()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();
        var refreshTokenService = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        var refreshToken = refreshTokenService.Create(Guid.CreateVersion7(_now.UtcDateTime));

        // Act
        await service.LogoutAsync(
            refreshToken.Value,
            TestContext.Current.CancellationToken);

        // Assert
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LogoutAsync_WhenSessionIsAlreadyRevoked_RemainsIdempotent()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        var email = "logout-idempotent@example.fr";
        await CreateUserAsync(
            factory,
            email);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            email);
        var refreshToken = GetRefreshToken(loginResponse);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();
        await service.LogoutAsync(
            refreshToken,
            TestContext.Current.CancellationToken);

        // Act
        await service.LogoutAsync(
            refreshToken,
            TestContext.Current.CancellationToken);

        // Assert
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        AssertTimestampClose(
            _now.UtcDateTime,
            session.RevokedAt);
    }

    [Fact]
    public async Task LogoutAsync_WhenSessionIsExpired_RevokesSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var email = "logout-expired@example.fr";
        await CreateUserAsync(
            factory,
            email);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            email);
        var refreshToken = GetRefreshToken(loginResponse);
        timeProvider.UtcNow = _now.AddHours(9);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();

        // Act
        await service.LogoutAsync(
            refreshToken,
            TestContext.Current.CancellationToken);

        // Assert
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        AssertTimestampClose(
            timeProvider.UtcNow.UtcDateTime,
            session.RevokedAt);
    }

    [Fact]
    public async Task LogoutAsync_WhenTokenIsAltered_RevokesIdentifiedSession()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        var email = "logout-altered@example.fr";
        await CreateUserAsync(
            factory,
            email);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            email);
        var refreshToken = GetRefreshToken(loginResponse);
        var sessionId = refreshToken.Split('.')[0];
        var alteredToken = $"{sessionId}.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();

        // Act
        await service.LogoutAsync(
            alteredToken,
            TestContext.Current.CancellationToken);

        // Assert
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        AssertTimestampClose(
            _now.UtcDateTime,
            session.RevokedAt);
    }

    [Fact]
    public async Task LogoutAsync_WhenMemberWasDeleted_RemainsIdempotentWithoutSession()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        var email = "logout-deleted-member@example.fr";
        var user = await CreateUserAsync(
            factory,
            email);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            email);
        var refreshToken = GetRefreshToken(loginResponse);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(value => value.Id == user.Id)
            .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        var service = scope.ServiceProvider.GetRequiredService<IAccountSessionService>();

        // Act
        await service.LogoutAsync(
            refreshToken,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LogoutAsync_WhenRefreshRunsConcurrently_LeavesSessionRevoked()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        var email = "logout-concurrent@example.fr";
        await CreateUserAsync(
            factory,
            email);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            email);
        var refreshToken = GetRefreshToken(loginResponse);
        await using var refreshScope = factory.Services.CreateAsyncScope();
        await using var logoutScope = factory.Services.CreateAsyncScope();
        var refreshService = refreshScope.ServiceProvider.GetRequiredService<IAccountSessionService>();
        var logoutService = logoutScope.ServiceProvider.GetRequiredService<IAccountSessionService>();

        // Act
        var refreshTask = refreshService.RefreshAsync(
            refreshToken,
            TestContext.Current.CancellationToken);
        var logoutTask = logoutService.LogoutAsync(
            refreshToken,
            TestContext.Current.CancellationToken);
        await Task.WhenAll(
            refreshTask,
            logoutTask);

        // Assert
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(session.RevokedAt);
    }

    private async Task<PostgreSqlApiFactory> CreateMigratedFactoryAsync(TimeProvider timeProvider)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            timeProvider);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE public.users CASCADE;",
            TestContext.Current.CancellationToken);

        return factory;
    }

    private static async Task<MonKadoUser> CreateUserAsync(
        PostgreSqlApiFactory factory,
        string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = new MonKadoUser
        {
            Id = Guid.CreateVersion7(_now.UtcDateTime),
            Email = email,
            UserName = email,
            DisplayName = "Logout test",
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(
            user,
            Password);
        Assert.True(
            result.Succeeded,
            string.Join(
                ", ",
                result.Errors.Select(error => error.Description)));
        var roleResult = await userManager.AddToRoleAsync(
            user,
            RoleNames.Member);
        Assert.True(
            roleResult.Succeeded,
            string.Join(
                ", ",
                roleResult.Errors.Select(error => error.Description)));

        return user;
    }

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions")
        {
            Content = JsonContent.Create(new
            {
                email,
                password = Password,
                rememberMe = false
            })
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> LogoutAsync(HttpClient client)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/v1/auth/sessions/current");
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions/refresh");
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> RefreshAsync(
        HttpClient client,
        string csrfToken,
        string cookies)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions/refresh");
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);
        request.Headers.Add(
            "Cookie",
            cookies);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task<string> GetCsrfTokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The CSRF token response is empty.");

        return payload.Token;
    }

    private static async Task<CsrfExchange> GetCsrfExchangeAsync(PostgreSqlApiFactory factory)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false
        });
        using var response = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        var payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The CSRF token response is empty.");
        var cookie = response.Headers.GetValues("Set-Cookie")
            .Select(GetCookiePair)
            .Single(value => value.StartsWith(
                "MonKado.Antiforgery=",
                StringComparison.Ordinal));

        return new CsrfExchange(
            client,
            payload.Token,
            cookie);
    }

    private static async Task<AccessTokenResponse> ReadAccessTokenAsync(HttpResponseMessage response)
    {
        return await response.Content.ReadFromJsonAsync<AccessTokenResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The access token response is empty.");
    }

    private static Guid GetSessionId(HttpResponseMessage response)
    {
        return Guid.ParseExact(
            GetRefreshToken(response).Split('.')[0],
            "N");
    }

    private static string GetRefreshToken(HttpResponseMessage response)
    {
        return GetRefreshCookiePair(response).Split(
            '=',
            2)[1];
    }

    private static string GetRefreshCookiePair(HttpResponseMessage response)
    {
        var value = response.Headers.GetValues("Set-Cookie").Single(header =>
            header.StartsWith(
                "MonKado.Refresh=",
                StringComparison.Ordinal) &&
            !header.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));

        return GetCookiePair(value);
    }

    private static string GetCookiePair(string value)
    {
        return value.Split(';')[0];
    }

    private static void AssertTimestampClose(
        DateTime expected,
        DateTime? actual)
    {
        var value = Assert.IsType<DateTime>(actual);
        Assert.Equal(
            expected,
            value,
            TimeSpan.FromMicroseconds(1));
    }
}
