using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Handlers;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class LoginIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Password = "  a long secure password  ";
    private static readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task AuthenticateWithAccountLockAsync_WhenAccountWasDeleted_ReturnsInvalidCredentials()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(new FixedTimeProvider(_now));
        await using var scope = factory.Services.CreateAsyncScope();
        var service = Assert.IsType<AccountSessionService>(
            scope.ServiceProvider.GetRequiredService<IAccountSessionService>());
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var executionStrategy = context.Database.CreateExecutionStrategy();

        // Act
        var attempt = await executionStrategy.ExecuteAsync(() =>
            service.AuthenticateWithAccountLockAsync(
                Guid.CreateVersion7(),
                "MISSING@EXAMPLE.FR",
                Password,
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            AccountLoginResult.InvalidCredentials,
            attempt.Result);
        Assert.Null(attempt.User);
    }

    [Fact]
    public async Task LoginAsync_WhenAccountDoesNotExist_ReturnsInvalidCredentials()
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
    }

    [Fact]
    public async Task LoginAsync_WhenSuccessfulAfterOneFailure_ResetsFailedCount()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        using var failedResponse = await LoginAsync(
            client,
            "lea@example.fr",
            "wrong password",
            rememberMe: false);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            failedResponse.StatusCode);

        // Act
        using var response = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var persistedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(
                candidate => candidate.Id == user.Id,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            0,
            persistedUser.AccessFailedCount);
    }

    [Fact]
    public async Task LoginAsync_WhenConfirmedAccount_CreatesRetrievableServerSideSession()
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
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var sessionCookie = GetIssuedSessionCookie(response);
        Assert.Contains(
            "; path=/",
            sessionCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "; httponly",
            sessionCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "; samesite=lax",
            sessionCookie,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "; expires=",
            sessionCookie,
            StringComparison.OrdinalIgnoreCase);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            user.Id,
            session.UserId);
        AssertTimestampClose(
            _now.UtcDateTime,
            session.CreatedAt,
            TimeSpan.FromMilliseconds(1));
        AssertTimestampClose(
            _now.UtcDateTime,
            session.RenewedAt,
            TimeSpan.FromMilliseconds(1));
        AssertTimestampClose(
            _now.UtcDateTime.AddHours(8),
            session.ExpiresAt,
            TimeSpan.FromSeconds(1));
        Assert.NotEmpty(session.ProtectedTicket);

        var ticketStore = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        var ticket = await ticketStore.RetrieveAsync(
            session.Id.ToString("N"),
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The stored authentication ticket is missing.");
        Assert.Equal(
            user.Id.ToString(),
            ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.NotNull(ticket.Properties.ExpiresUtc);
        AssertTimestampClose(
            _now.AddHours(8),
            ticket.Properties.ExpiresUtc.Value,
            TimeSpan.FromSeconds(1));
        Assert.False(ticket.Properties.IsPersistent);
        Assert.True(ticket.Properties.AllowRefresh);
    }

    [Fact]
    public async Task LoginAsync_WhenRememberMeRotatesCurrentSessionWithThirtyDayPersistentSession_Completes()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        // Act
        using var firstResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            firstResponse.StatusCode);
        var firstSessionId = await GetOnlySessionIdAsync(factory);

        using var secondResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: true);

        Assert.Equal(
            HttpStatusCode.NoContent,
            secondResponse.StatusCode);
        var sessionCookie = GetIssuedSessionCookie(secondResponse);
        Assert.Contains(
            "; expires=",
            sessionCookie,
            StringComparison.OrdinalIgnoreCase);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(
            firstSessionId,
            session.Id);
        AssertTimestampClose(
            _now.UtcDateTime.AddDays(30),
            session.ExpiresAt,
            TimeSpan.FromSeconds(1));

        var ticketStore = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        Assert.Null(await ticketStore.RetrieveAsync(
            firstSessionId.ToString("N"),
            TestContext.Current.CancellationToken));
        var replacement = await ticketStore.RetrieveAsync(
            session.Id.ToString("N"),
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The replacement authentication ticket is missing.");
        Assert.True(replacement.Properties.IsPersistent);
        Assert.False(replacement.Properties.AllowRefresh);
    }

    [Fact]
    public async Task LoginAsync_WhenRenew_DoesNotRecreateARevokedSession()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        using var client = factory.CreateClient();
        // Act
        using var response = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var ticketStore = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        var sessionId = await GetOnlySessionIdAsync(factory);
        var ticket = await ticketStore.RetrieveAsync(
            sessionId.ToString("N"),
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The authentication ticket is missing.");

        await ticketStore.RemoveAsync(
            sessionId.ToString("N"),
            TestContext.Current.CancellationToken);
        await ticketStore.RenewAsync(
            sessionId.ToString("N"),
            ticket,
            TestContext.Current.CancellationToken);

        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.False(await context.AuthenticationSessions.AnyAsync(
            session => session.Id == sessionId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoginAsync_WhenFiveInvalidPasswordsLockAccountWithoutCreatingSession_Completes()
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
            using var response = await LoginAsync(
                client,
                "lea@example.fr",
                "wrong password",
                rememberMe: false);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                response.StatusCode);
            Assert.Equal(
                ErrorCodes.AccountInvalidCredentials,
                await GetErrorCodeAsync(response));
        }

        // Act
        using var lockedResponse = await LoginAsync(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            lockedResponse.StatusCode);
        Assert.Equal(
            ErrorCodes.AccountInvalidCredentials,
            await GetErrorCodeAsync(lockedResponse));

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var persistedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(
                value => value.Id == user.Id,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            0,
            persistedUser.AccessFailedCount);
        Assert.NotNull(persistedUser.LockoutEnd);
        Assert.InRange(
            persistedUser.LockoutEnd.Value,
            DateTimeOffset.UtcNow.AddMinutes(14),
            DateTimeOffset.UtcNow.AddMinutes(16));
        Assert.Empty(await context.AuthenticationSessions.ToArrayAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoginAsync_WhenFiveConcurrentInvalidPasswordsLockAccountWithoutLosingAttempts_Completes()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        var user = await CreateUserAsync(
            factory,
            "lea@example.fr",
            emailConfirmed: true);
        var clients = Enumerable.Range(
            0,
            5)
            .Select(_ => factory.CreateClient())
            .ToArray();
        try
        {
            // Act
            var responses = await Task.WhenAll(clients.Select(client => LoginAsync(
                client,
                "lea@example.fr",
                "wrong password",
                rememberMe: false)));

            // Assert
            foreach (var response in responses)
            {
                using (response)
                {
                    Assert.Equal(
                        HttpStatusCode.Unauthorized,
                        response.StatusCode);
                    Assert.Equal(
                        ErrorCodes.AccountInvalidCredentials,
                        await GetErrorCodeAsync(response));
                }
            }

            using var verificationClient = factory.CreateClient();
            using var lockedResponse = await LoginAsync(
                verificationClient,
                "lea@example.fr",
                Password,
                rememberMe: false);
            Assert.Equal(
                HttpStatusCode.Unauthorized,
                lockedResponse.StatusCode);

            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            var persistedUser = await context.Users
                .AsNoTracking()
                .SingleAsync(
                    value => value.Id == user.Id,
                    TestContext.Current.CancellationToken);
            Assert.Equal(
                0,
                persistedUser.AccessFailedCount);
            Assert.NotNull(persistedUser.LockoutEnd);
            Assert.Empty(await context.AuthenticationSessions.ToArrayAsync(
                TestContext.Current.CancellationToken));
        }
        finally
        {
            foreach (var client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task LoginAsync_WhenUnconfirmedAndExpiredAccountsReturnTheExpectedProblems_Completes()
    {
        // Arrange
        var timeProvider = new FixedTimeProvider(_now);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        await CreateUserAsync(
            factory,
            "pending@example.fr",
            emailConfirmed: false,
            expiresAt: _now.UtcDateTime.AddDays(1));
        await CreateUserAsync(
            factory,
            "expired@example.fr",
            emailConfirmed: false,
            expiresAt: _now.UtcDateTime);
        using var pendingClient = factory.CreateClient();
        using var expiredClient = factory.CreateClient();

        using var pendingResponse = await LoginAsync(
            pendingClient,
            "pending@example.fr",
            Password,
            rememberMe: false);
        // Act
        using var expiredResponse = await LoginAsync(
            expiredClient,
            "expired@example.fr",
            Password,
            rememberMe: false);

        // Assert
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            pendingResponse.StatusCode);
        Assert.Equal(
            ErrorCodes.AccountEmailNotConfirmed,
            await GetErrorCodeAsync(pendingResponse));
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            expiredResponse.StatusCode);
        Assert.Equal(
            ErrorCodes.AccountInvalidCredentials,
            await GetErrorCodeAsync(expiredResponse));
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.AuthenticationSessions.ToArrayAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task LoginAsync_WhenCleanup_DeletesOnlySessionsExpiredAtCutoff()
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
        // Act
        using var persistentResponse = await LoginAsync(
            persistentClient,
            "persistent@example.fr",
            Password,
            rememberMe: true);
        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            shortResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            persistentResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var cleanup =
            scope.ServiceProvider.GetRequiredService<IExpiredAuthenticationSessionCleanup>();
        var deletedCount = await cleanup.DeleteExpiredSessionsAsync(
            _now.UtcDateTime.AddHours(8),
            500,
            TestContext.Current.CancellationToken);

        Assert.Equal(
            1,
            deletedCount);
        Assert.Equal(
            0,
            await cleanup.DeleteExpiredSessionsAsync(
                _now.UtcDateTime.AddHours(8),
                500,
                TestContext.Current.CancellationToken));
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var remaining = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        AssertTimestampClose(
            _now.UtcDateTime.AddDays(30),
            remaining.ExpiresAt,
            TimeSpan.FromSeconds(1));
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
        bool emailConfirmed,
        DateTime? expiresAt = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = new MonKadoUser()
        {
            Id = Guid.CreateVersion7(_now),
            Email = email,
            UserName = email,
            DisplayName = "Login test",
            EmailConfirmed = emailConfirmed,
            UnconfirmedAccountExpiresAt = emailConfirmed
                ? null
                : expiresAt ?? _now.UtcDateTime.AddDays(30)
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

    private static async Task<Guid> GetOnlySessionIdAsync(PostgreSqlApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.AuthenticationSessions
            .Select(session => session.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
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

    private static string GetIssuedSessionCookie(HttpResponseMessage response)
    {

        return response.Headers.GetValues("Set-Cookie").Single(header =>
            header.StartsWith(
                "MonKado.Auth=",
                StringComparison.Ordinal) &&
            !header.StartsWith(
                "MonKado.Auth=;",
                StringComparison.Ordinal));
    }

    private static async Task<string?> GetErrorCodeAsync(HttpResponseMessage response)
    {
        using var document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The authentication problem response is empty.");

        return document.RootElement.GetProperty("errorCode").GetString();
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

    private static void AssertTimestampClose(
        DateTimeOffset expected,
        DateTimeOffset actual,
        TimeSpan tolerance)
    {
        Assert.InRange(
            actual,
            expected.Subtract(tolerance),
            expected.Add(tolerance));
    }

}
