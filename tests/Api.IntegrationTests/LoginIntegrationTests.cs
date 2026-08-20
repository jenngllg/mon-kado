using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using JennGllg.Fr.MonKado.Back.Api.Security;
using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public sealed class LoginIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Password = "  a long secure password  ";
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task ConfirmedAccountCreatesRetrievableServerSideSession()
    {
        FixedTimeProvider timeProvider = new(Now);
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(timeProvider);
        MonKadoUser user = await CreateUser(factory, "lea@example.fr", emailConfirmed: true);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await Login(
            client,
            " LEA@example.fr ",
            Password,
            rememberMe: false);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        string sessionCookie = GetIssuedSessionCookie(response);
        Assert.Contains("; path=/", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; httponly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("; samesite=lax", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("; expires=", sessionCookie, StringComparison.OrdinalIgnoreCase);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        AuthenticationSession session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(user.Id, session.UserId);
        AssertTimestampClose(Now, session.CreatedAt, TimeSpan.FromMilliseconds(1));
        AssertTimestampClose(Now, session.RenewedAt, TimeSpan.FromMilliseconds(1));
        AssertTimestampClose(Now.AddHours(8), session.ExpiresAt, TimeSpan.FromSeconds(1));
        Assert.NotEmpty(session.ProtectedTicket);

        ITicketStore ticketStore = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        AuthenticationTicket ticket = await ticketStore.RetrieveAsync(
            session.Id.ToString("N"),
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The stored authentication ticket is missing.");
        Assert.Equal(
            user.Id.ToString(),
            ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.NotNull(ticket.Properties.ExpiresUtc);
        AssertTimestampClose(
            Now.AddHours(8),
            ticket.Properties.ExpiresUtc.Value, TimeSpan.FromSeconds(1));
        Assert.False(ticket.Properties.IsPersistent);
        Assert.True(ticket.Properties.AllowRefresh);
    }

    [Fact]
    public async Task RememberMeRotatesCurrentSessionWithThirtyDayPersistentSession()
    {
        FixedTimeProvider timeProvider = new(Now);
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(timeProvider);
        await CreateUser(factory, "lea@example.fr", emailConfirmed: true);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage firstResponse = await Login(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        Assert.Equal(HttpStatusCode.NoContent, firstResponse.StatusCode);
        Guid firstSessionId = await GetOnlySessionId(factory);

        using HttpResponseMessage secondResponse = await Login(
            client,
            "lea@example.fr",
            Password,
            rememberMe: true);

        Assert.Equal(HttpStatusCode.NoContent, secondResponse.StatusCode);
        string sessionCookie = GetIssuedSessionCookie(secondResponse);
        Assert.Contains("; expires=", sessionCookie, StringComparison.OrdinalIgnoreCase);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        AuthenticationSession session = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(firstSessionId, session.Id);
        AssertTimestampClose(Now.AddDays(30), session.ExpiresAt, TimeSpan.FromSeconds(1));

        ITicketStore ticketStore = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        Assert.Null(await ticketStore.RetrieveAsync(
            firstSessionId.ToString("N"),
            TestContext.Current.CancellationToken));
        AuthenticationTicket replacement = await ticketStore.RetrieveAsync(
            session.Id.ToString("N"),
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The replacement authentication ticket is missing.");
        Assert.True(replacement.Properties.IsPersistent);
        Assert.False(replacement.Properties.AllowRefresh);
    }

    [Fact]
    public async Task RenewDoesNotRecreateARevokedSession()
    {
        FixedTimeProvider timeProvider = new(Now);
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(timeProvider);
        await CreateUser(factory, "lea@example.fr", emailConfirmed: true);
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await Login(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        ITicketStore ticketStore = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        Guid sessionId = await GetOnlySessionId(factory);
        AuthenticationTicket ticket = await ticketStore.RetrieveAsync(
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

        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.False(await context.AuthenticationSessions.AnyAsync(
            session => session.Id == sessionId,
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FiveInvalidPasswordsLockAccountWithoutCreatingSession()
    {
        FixedTimeProvider timeProvider = new(Now);
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(timeProvider);
        MonKadoUser user = await CreateUser(factory, "lea@example.fr", emailConfirmed: true);
        using HttpClient client = factory.CreateClient();

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            using HttpResponseMessage response = await Login(
                client,
                "lea@example.fr",
                "wrong password",
                rememberMe: false);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("INVALID_CREDENTIALS", await GetProblemCode(response));
        }

        using HttpResponseMessage lockedResponse = await Login(
            client,
            "lea@example.fr",
            Password,
            rememberMe: false);
        Assert.Equal(HttpStatusCode.Unauthorized, lockedResponse.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await GetProblemCode(lockedResponse));

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        MonKadoUser persistedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(value => value.Id == user.Id, TestContext.Current.CancellationToken);
        Assert.Equal(0, persistedUser.AccessFailedCount);
        Assert.NotNull(persistedUser.LockoutEnd);
        Assert.InRange(
            persistedUser.LockoutEnd.Value,
            DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
        Assert.Empty(await context.AuthenticationSessions.ToArrayAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task FiveConcurrentInvalidPasswordsLockAccountWithoutLosingAttempts()
    {
        FixedTimeProvider timeProvider = new(Now);
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(timeProvider);
        MonKadoUser user = await CreateUser(factory, "lea@example.fr", emailConfirmed: true);
        HttpClient[] clients = Enumerable.Range(0, 5)
            .Select(_ => factory.CreateClient())
            .ToArray();
        try
        {
            HttpResponseMessage[] responses = await Task.WhenAll(clients.Select(client => Login(
                client,
                "lea@example.fr",
                "wrong password",
                rememberMe: false)));
            foreach (HttpResponseMessage response in responses)
            {
                using (response)
                {
                    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
                    Assert.Equal("INVALID_CREDENTIALS", await GetProblemCode(response));
                }
            }

            using HttpClient verificationClient = factory.CreateClient();
            using HttpResponseMessage lockedResponse = await Login(
                verificationClient,
                "lea@example.fr",
                Password,
                rememberMe: false);
            Assert.Equal(HttpStatusCode.Unauthorized, lockedResponse.StatusCode);

            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            MonKadoUser persistedUser = await context.Users
                .AsNoTracking()
                .SingleAsync(value => value.Id == user.Id, TestContext.Current.CancellationToken);
            Assert.Equal(0, persistedUser.AccessFailedCount);
            Assert.NotNull(persistedUser.LockoutEnd);
            Assert.Empty(await context.AuthenticationSessions.ToArrayAsync(
                TestContext.Current.CancellationToken));
        }
        finally
        {
            foreach (HttpClient client in clients)
            {
                client.Dispose();
            }
        }
    }

    [Fact]
    public async Task UnconfirmedAndExpiredAccountsReturnTheExpectedProblems()
    {
        FixedTimeProvider timeProvider = new(Now);
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(timeProvider);
        await CreateUser(factory, "pending@example.fr", emailConfirmed: false, expiresAt: Now.AddDays(1));
        await CreateUser(factory, "expired@example.fr", emailConfirmed: false, expiresAt: Now);
        using HttpClient pendingClient = factory.CreateClient();
        using HttpClient expiredClient = factory.CreateClient();

        using HttpResponseMessage pendingResponse = await Login(
            pendingClient,
            "pending@example.fr",
            Password,
            rememberMe: false);
        using HttpResponseMessage expiredResponse = await Login(
            expiredClient,
            "expired@example.fr",
            Password,
            rememberMe: false);

        Assert.Equal(HttpStatusCode.Unauthorized, pendingResponse.StatusCode);
        Assert.Equal("EMAIL_NOT_CONFIRMED", await GetProblemCode(pendingResponse));
        Assert.Equal(HttpStatusCode.Unauthorized, expiredResponse.StatusCode);
        Assert.Equal("INVALID_CREDENTIALS", await GetProblemCode(expiredResponse));
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.AuthenticationSessions.ToArrayAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CleanupDeletesOnlySessionsExpiredAtCutoff()
    {
        FixedTimeProvider timeProvider = new(Now);
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(timeProvider);
        await CreateUser(factory, "short@example.fr", emailConfirmed: true);
        await CreateUser(factory, "persistent@example.fr", emailConfirmed: true);
        using HttpClient shortClient = factory.CreateClient();
        using HttpClient persistentClient = factory.CreateClient();
        using HttpResponseMessage shortResponse = await Login(
            shortClient,
            "short@example.fr",
            Password,
            rememberMe: false);
        using HttpResponseMessage persistentResponse = await Login(
            persistentClient,
            "persistent@example.fr",
            Password,
            rememberMe: true);
        Assert.Equal(HttpStatusCode.NoContent, shortResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, persistentResponse.StatusCode);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        IExpiredAuthenticationSessionCleanup cleanup =
            scope.ServiceProvider.GetRequiredService<IExpiredAuthenticationSessionCleanup>();
        int deletedCount = await cleanup.DeleteExpiredSessionsAsync(
            Now.AddHours(8),
            500,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, deletedCount);
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        AuthenticationSession remaining = await context.AuthenticationSessions
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        AssertTimestampClose(Now.AddDays(30), remaining.ExpiresAt, TimeSpan.FromSeconds(1));
    }

    private async Task<PostgreSqlApiFactory> CreateMigratedFactory(TimeProvider timeProvider)
    {
        PostgreSqlApiFactory factory = new(fixture.Container.GetConnectionString(), timeProvider);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE public.users CASCADE;",
            TestContext.Current.CancellationToken);
        return factory;
    }

    private static async Task<MonKadoUser> CreateUser(
        PostgreSqlApiFactory factory,
        string email,
        bool emailConfirmed,
        DateTimeOffset? expiresAt = null)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        UserManager<MonKadoUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        MonKadoUser user = new()
        {
            Id = Guid.CreateVersion7(Now),
            Email = email,
            UserName = email,
            DisplayName = "Login test",
            EmailConfirmed = emailConfirmed,
            CreatedAt = Now,
            UpdatedAt = Now,
            UnconfirmedAccountExpiresAt = emailConfirmed ? null : expiresAt ?? Now.AddDays(30)
        };
        IdentityResult result = await userManager.CreateAsync(user, Password);
        Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(error => error.Description)));
        return user;
    }

    private static async Task<Guid> GetOnlySessionId(PostgreSqlApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        return await context.AuthenticationSessions
            .Select(session => session.Id)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<HttpResponseMessage> Login(
        HttpClient client,
        string email,
        string password,
        bool rememberMe)
    {
        string csrfToken = await GetCsrfToken(client);
        HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/sessions")
        {
            Content = JsonContent.Create(new { email, password, rememberMe })
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task<string> GetCsrfToken(HttpClient client)
    {
        using HttpResponseMessage response = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        CsrfTokenResponse payload = await response.Content.ReadFromJsonAsync<CsrfTokenResponse>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The CSRF token response is empty.");
        return payload.Token;
    }

    private static string GetIssuedSessionCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(header =>
            header.StartsWith("MonKado.Auth=", StringComparison.Ordinal) &&
            !header.StartsWith("MonKado.Auth=;", StringComparison.Ordinal));

    private static async Task<string?> GetProblemCode(HttpResponseMessage response)
    {
        using JsonDocument document = await response.Content.ReadFromJsonAsync<JsonDocument>(
            TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The authentication problem response is empty.");
        return document.RootElement.GetProperty("code").GetString();
    }

    private static void AssertTimestampClose(
        DateTimeOffset expected,
        DateTimeOffset actual,
        TimeSpan tolerance) =>
        Assert.InRange(actual, expected.Subtract(tolerance), expected.Add(tolerance));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
