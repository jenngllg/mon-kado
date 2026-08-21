using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class LoginIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Password = "  a long secure password  ";
    private static readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task LoginAsync_WhenAccountDoesNotExist_ReturnsUnauthorizedWithoutSession()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        using var client = factory.CreateClient();

        // Act
        using var response = await LoginAsync(
            client,
            "missing@example.fr",
            Password,
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.AccountInvalidCredentials,
            await GetErrorCodeAsync(response));
        Assert.False(response.Headers.TryGetValues(
            "Set-Cookie",
            out var cookies) && cookies.Any(value => value.StartsWith(
                "MonKado.Refresh=",
                StringComparison.Ordinal)));
    }

    [Fact]
    public async Task LoginAsync_WhenConfirmedAccount_ReturnsMinimalJwtAndStoresRefreshHash()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();

        // Act
        using var response = await LoginAsync(
            client,
            " LEA@example.fr ",
            Password,
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var payload = await ReadAccessTokenAsync(response);
        Assert.Equal(
            "Bearer",
            payload.TokenType);
        Assert.Equal(
            900,
            payload.ExpiresIn);
        AssertMinimalToken(
            payload.AccessToken,
            user.Id);

        var refreshCookie = GetRefreshCookie(response);
        Assert.Contains(
            "; path=/",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "; httponly",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "; samesite=strict",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "; expires=",
            refreshCookie,
            StringComparison.OrdinalIgnoreCase);

        var refreshToken = GetCookieValue(refreshCookie);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            user.Id,
            session.UserId);
        Assert.False(session.IsPersistent);
        Assert.Null(session.RevokedAt);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)),
            session.RefreshTokenHash);
        Assert.DoesNotContain(
            refreshToken,
            Convert.ToHexString(session.RefreshTokenHash),
            StringComparison.Ordinal);
        AssertTimestampClose(
            _now.UtcDateTime.AddHours(8),
            session.ExpiresAt,
            TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RefreshAsync_WhenSessionIsValid_RotatesTokenAndSlidesExpiration()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        var firstToken = GetCookieValue(GetRefreshCookie(loginResponse));
        timeProvider.UtcNow = _now.AddMinutes(10);

        // Act
        using var response = await RefreshAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        var secondToken = GetCookieValue(GetRefreshCookie(response));
        Assert.NotEqual(
            firstToken,
            secondToken);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(secondToken)),
            session.RefreshTokenHash);
        AssertTimestampClose(
            timeProvider.UtcNow.UtcDateTime,
            session.RenewedAt,
            TimeSpan.FromSeconds(1));
        AssertTimestampClose(
            timeProvider.UtcNow.UtcDateTime.AddHours(8),
            session.ExpiresAt,
            TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RefreshAsync_WhenPreviousTokenIsReused_RevokesSessionAndDeletesCookie()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        var previousCookie = GetRefreshCookie(loginResponse);
        using var rotatedResponse = await RefreshAsync(client);
        Assert.Equal(
            HttpStatusCode.OK,
            rotatedResponse.StatusCode);
        var csrf = await GetCsrfExchangeAsync(factory);

        // Act
        using var response = await RefreshAsync(
            csrf.Client,
            csrf.Token,
            string.Join(
                "; ",
                csrf.Cookie,
                GetCookiePair(previousCookie)));

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Equal(
            ErrorCodes.AccountAuthenticationSessionInvalid,
            await GetErrorCodeAsync(response));
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.NotNull((await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).RevokedAt);
        csrf.Client.Dispose();
    }

    [Fact]
    public async Task RefreshAsync_WhenTokenIsAltered_RevokesSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        var refreshToken = GetCookieValue(GetRefreshCookie(loginResponse));
        var sessionId = refreshToken.Split('.')[0];
        var alteredToken = $"{sessionId}.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var csrf = await GetCsrfExchangeAsync(factory);

        // Act
        using var response = await RefreshAsync(
            csrf.Client,
            csrf.Token,
            string.Join(
                "; ",
                csrf.Cookie,
                $"MonKado.Refresh={alteredToken}"));

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.NotNull((await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).RevokedAt);
        csrf.Client.Dispose();
    }

    [Fact]
    public async Task RefreshAsync_WhenSessionIsExpired_ReturnsUnauthorizedAndRevokesSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        timeProvider.UtcNow = _now.AddHours(8).AddSeconds(1);

        // Act
        using var response = await RefreshAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.NotNull((await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).RevokedAt);
    }

    [Fact]
    public async Task RefreshAsync_WhenMemberWasDeleted_ReturnsUnauthorizedWithoutSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.Users
                .Where(value => value.Id == user.Id)
                .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
        }

        // Act
        using var response = await RefreshAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await verificationContext.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RefreshAsync_WhenTokenFormatIsInvalid_ReturnsNullBeforeDatabaseLookup()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        await using var scope = factory.Services.CreateAsyncScope();
        var service = Assert.IsType<AccountSessionService>(
            scope.ServiceProvider.GetRequiredService<IAccountSessionService>());

        // Act
        var result = await service.RefreshAsync(
            "invalid-refresh-token",
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task AuthenticateWithAccountLockAsync_WhenAccountDisappeared_ReturnsInvalidCredentials()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        await using var scope = factory.Services.CreateAsyncScope();
        var service = Assert.IsType<AccountSessionService>(
            scope.ServiceProvider.GetRequiredService<IAccountSessionService>());
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var executionStrategy = context.Database.CreateExecutionStrategy();

        // Act
        var result = await executionStrategy.ExecuteAsync(() =>
            service.AuthenticateWithAccountLockAsync(
                Guid.NewGuid(),
                "MISSING@EXAMPLE.FR",
                Password,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            AccountLoginResult.InvalidCredentials,
            result.Result);
        Assert.Null(result.User);
    }

    [Theory]
    [InlineData(-1, ErrorCodes.AccountInvalidCredentials)]
    [InlineData(1, ErrorCodes.AccountEmailNotConfirmed)]
    [InlineData(null, ErrorCodes.AccountEmailNotConfirmed)]
    public async Task LoginAsync_WhenAccountIsUnconfirmed_ReturnsExpectedUnauthorized(
        int? expirationDays,
        string expectedErrorCode)
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: false);
        var expiresAt = expirationDays is null
            ? (DateTime?)null
            : _now.UtcDateTime.AddDays(Math.Max(
                1,
                expirationDays.Value));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.Users
                .Where(value => value.Id == user.Id)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        value => value.UnconfirmedAccountExpiresAt,
                        expiresAt),
                    TestContext.Current.CancellationToken);
        }

        if (expirationDays < 0)
            timeProvider.UtcNow = _now.AddDays(2);

        using var client = factory.CreateClient();

        // Act
        using var response = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        Assert.Equal(
            expectedErrorCode,
            await GetErrorCodeAsync(response));
    }

    [Fact]
    public async Task LoginAsync_WhenPreviousFailureExists_ResetsFailureCount()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
            var persistedUser = await userManager.FindByIdAsync(user.Id.ToString("D"));
            Assert.NotNull(persistedUser);
            Assert.True((await userManager.AccessFailedAsync(persistedUser)).Succeeded);
        }
        using var client = factory.CreateClient();

        // Act
        using var response = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            0,
            (await context.Users
                .AsNoTracking()
                .SingleAsync(TestContext.Current.CancellationToken)).AccessFailedCount);
    }

    [Fact]
    public async Task RefreshAsync_WhenMemberRowIsMissing_RevokesOrphanedSession()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.Database.OpenConnectionAsync(TestContext.Current.CancellationToken);

            try
            {
                await context.Database.ExecuteSqlRawAsync(
                    "SET session_replication_role = replica;",
                    TestContext.Current.CancellationToken);
                await context.Users
                    .Where(value => value.Id == user.Id)
                    .ExecuteDeleteAsync(TestContext.Current.CancellationToken);
            }
            finally
            {
                await context.Database.ExecuteSqlRawAsync(
                    "SET session_replication_role = origin;",
                    TestContext.Current.CancellationToken);
                await context.Database.CloseConnectionAsync();
            }
        }

        // Act
        using var response = await RefreshAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        await using var verificationScope = factory.Services.CreateAsyncScope();
        var verificationContext = verificationScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.NotNull((await verificationContext.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken)).RevokedAt);
    }

    [Fact]
    public async Task RevokeCurrentSessionAsync_WhenTokenCannotIdentifyActiveSession_DoesNotChangeSession()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        var currentToken = GetCookieValue(GetRefreshCookie(loginResponse));
        await using var scope = factory.Services.CreateAsyncScope();
        var service = Assert.IsType<AccountSessionService>(
            scope.ServiceProvider.GetRequiredService<IAccountSessionService>());
        var refreshTokenService = scope.ServiceProvider.GetRequiredService<IRefreshTokenService>();
        Assert.True(refreshTokenService.TryGetSessionId(
            currentToken,
            out var sessionId));
        var mismatchedToken = refreshTokenService.Create(sessionId).Value;
        var unknownToken = refreshTokenService.Create(Guid.NewGuid()).Value;

        // Act
        await service.RevokeCurrentSessionAsync(
            "invalid-refresh-token",
            _now.UtcDateTime,
            TestContext.Current.CancellationToken);
        await service.RevokeCurrentSessionAsync(
            unknownToken,
            _now.UtcDateTime,
            TestContext.Current.CancellationToken);
        await service.RevokeCurrentSessionAsync(
            mismatchedToken,
            _now.UtcDateTime,
            TestContext.Current.CancellationToken);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .SingleAsync(TestContext.Current.CancellationToken);
        var revokedAt = _now.UtcDateTime.AddMinutes(1);
        session.Revoke(revokedAt);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await service.RevokeCurrentSessionAsync(
            currentToken,
            _now.UtcDateTime.AddMinutes(2),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            revokedAt,
            session.RevokedAt);
    }

    [Fact]
    public async Task RefreshAsync_WhenRememberMe_RotatesWithoutExtendingAbsoluteExpiration()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var loginResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: true);
        Assert.Contains(
            "; expires=",
            GetRefreshCookie(loginResponse),
            StringComparison.OrdinalIgnoreCase);
        timeProvider.UtcNow = _now.AddDays(1);

        // Act
        using var response = await RefreshAsync(client);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.True(session.IsPersistent);
        AssertTimestampClose(
            _now.UtcDateTime.AddDays(30),
            session.ExpiresAt,
            TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task LoginAsync_WhenSameBrowserSignsInAgain_RevokesOnlyPreviousSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var firstBrowser = factory.CreateClient();
        using var secondBrowser = factory.CreateClient();
        using var firstResponse = await LoginAsync(
            firstBrowser,
            "lea@example.fr",
            Password,
            rememberMe: false);
        using var otherBrowserResponse = await LoginAsync(
            secondBrowser,
            "lea@example.fr",
            Password,
            rememberMe: false);

        // Act
        using var replacementResponse = await LoginAsync(
            firstBrowser,
            "lea@example.fr",
            Password,
            rememberMe: true);

        // Assert
        Assert.Equal(
            HttpStatusCode.OK,
            replacementResponse.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var sessions = await context.AuthenticationSessions
            .AsNoTracking()
            .OrderBy(session => session.CreatedAt)
            .ThenBy(session => session.Id)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            3,
            sessions.Length);
        Assert.Single(
            sessions,
            session => session.RevokedAt is not null);
        Assert.Equal(
            2,
            sessions.Count(session => session.RevokedAt is null));
    }

    [Fact]
    public async Task LoginAsync_WhenFiveInvalidPasswords_LocksAccountWithoutCreatingSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            using var failedResponse = await LoginAsync(
                client,
                "lea@example.fr",
                "wrong password",
                rememberMe: false);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                failedResponse.StatusCode);
        }

        // Act
        using var response = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var persistedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == user.Id,
                TestContext.Current.CancellationToken);
        Assert.NotNull(persistedUser.LockoutEnd);
        Assert.Empty(await context.AuthenticationSessions.ToArrayAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeleteExpiredSessionsAsync_WhenCutoffReached_DeletesOnlyExpiredSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "short@example.fr",
            emailConfirmed: true);
        await CreateUserAsync(
            factory,
            "persistent@example.fr",
            emailConfirmed: true);
        using var shortClient = factory.CreateClient();
        using var persistentClient = factory.CreateClient();
        using var shortResponse = await LoginAsync(
            shortClient,
            "short@example.fr",
            Password,
            rememberMe: false);
        using var persistentResponse = await LoginAsync(
            persistentClient,
            "persistent@example.fr",
            Password,
            rememberMe: true);
        await using var scope = factory.Services.CreateAsyncScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<IExpiredAuthenticationSessionCleanup>();

        // Act
        var deletedCount = await cleanup.DeleteExpiredSessionsAsync(
            _now.UtcDateTime.AddHours(8),
            500,
            TestContext.Current.CancellationToken);
        var secondDeletedCount = await cleanup.DeleteExpiredSessionsAsync(
            _now.UtcDateTime.AddHours(8),
            500,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            1,
            deletedCount);
        Assert.Equal(
            0,
            secondDeletedCount);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Single(await context.AuthenticationSessions
            .AsNoTracking()
            .ToArrayAsync(TestContext.Current.CancellationToken));
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
        string email,
        bool emailConfirmed)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = new MonKadoUser
        {
            Id = Guid.CreateVersion7(_now),
            Email = email,
            UserName = email,
            DisplayName = "Login test",
            EmailConfirmed = emailConfirmed,
            UnconfirmedAccountExpiresAt = emailConfirmed
                ? null
                : _now.UtcDateTime.AddDays(30)
        };
        var result = await userManager.CreateAsync(
            user,
            Password);
        Assert.True(
            result.Succeeded,
            string.Join(
                ", ",
                result.Errors.Select(error => error.Description)));

        return user;
    }

    private static async Task<HttpResponseMessage> LoginAsync(
        HttpClient client,
        string email,
        string password,
        bool rememberMe)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/sessions")
        {
            Content = JsonContent.Create(new
            {
                email,
                password,
                rememberMe
            })
        };
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
        var request = new HttpRequestMessage(
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
        var request = new HttpRequestMessage(
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

    private static string GetRefreshCookie(HttpResponseMessage response)
    {
        return response.Headers.GetValues("Set-Cookie").Single(header =>
            header.StartsWith(
                "MonKado.Refresh=",
                StringComparison.Ordinal) &&
            !header.StartsWith(
                "MonKado.Refresh=;",
                StringComparison.Ordinal));
    }

    private static string GetCookiePair(string value)
    {
        return value.Split(';')[0];
    }

    private static string GetCookieValue(string value)
    {
        return GetCookiePair(value).Split(
            '=',
            2)[1];
    }

    private static async Task<string?> GetErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The authentication error response is empty.");

        return document.RootElement.GetProperty("errorCode").GetString();
    }

    private static void AssertMinimalToken(
        string value,
        Guid expectedUserId)
    {
        var token = new JwtSecurityTokenHandler().ReadJwtToken(value);
        var claimTypes = token.Claims
            .Select(claim => claim.Type)
            .OrderBy(type => type)
            .ToArray();
        var expectedClaimTypes = new[]
        {
            JwtRegisteredClaimNames.Aud,
            JwtRegisteredClaimNames.Exp,
            JwtRegisteredClaimNames.Iat,
            JwtRegisteredClaimNames.Iss,
            JwtRegisteredClaimNames.Jti,
            JwtRegisteredClaimNames.Sub
        }
        .OrderBy(type => type)
        .ToArray();
        Assert.Equal(
            expectedClaimTypes,
            claimTypes);
        Assert.Equal(
            expectedUserId.ToString("D"),
            token.Subject);
    }

    private static void AssertTimestampClose(
        DateTime expected,
        DateTime actual,
        TimeSpan tolerance)
    {
        Assert.InRange(
            actual,
            expected.Subtract(tolerance),
            expected.Add(tolerance));
    }

}
