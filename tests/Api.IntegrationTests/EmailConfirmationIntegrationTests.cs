using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using JennGllg.Fr.MonKado.Back.Api.Security;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public sealed class EmailConfirmationIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Password = "a sufficiently long password";
    private static readonly DateTimeOffset ReferenceTime =
        new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ValidConfirmationUpdatesAccountCancelsOutboxAndCanBeReplayed()
    {
        MutableTimeProvider timeProvider = new(ReferenceTime);
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(timeProvider);
        using HttpClient client = factory.CreateClient();
        MonKadoUser user = await RegisterAndLoadUser(factory, client, "valid@example.fr");
        string token = await GenerateEncodedToken(factory, user);

        using HttpResponseMessage response = await Confirm(client, user.Id, token);
        using HttpResponseMessage replay = await Confirm(client, user.Id, token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, replay.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("Set-Cookie"));

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        MonKadoUser confirmed = await context.Users.AsNoTracking().SingleAsync(
            candidate => candidate.Id == user.Id,
            TestContext.Current.CancellationToken);
        AuthenticationEmailOutboxMessage outbox = await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.True(confirmed.EmailConfirmed);
        Assert.Null(confirmed.UnconfirmedAccountExpiresAt);
        Assert.Equal(ReferenceTime, confirmed.UpdatedAt);
        Assert.Equal(2, confirmed.Version);
        Assert.Equal(ReferenceTime, outbox.ProcessedAt);
    }

    [Fact]
    public async Task InvalidAlteredWrongUserAndExpiredAccountReturnTheSameProblem()
    {
        MutableTimeProvider timeProvider = new(ReferenceTime);
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(timeProvider);
        using HttpClient client = factory.CreateClient();
        MonKadoUser user = await RegisterAndLoadUser(factory, client, "invalid@example.fr");
        string token = await GenerateEncodedToken(factory, user);

        using HttpResponseMessage malformed = await Confirm(client, user.Id, "***");
        using HttpResponseMessage altered = await Confirm(client, user.Id, token[..^1] + "A");
        using HttpResponseMessage wrongUser = await Confirm(client, Guid.CreateVersion7(), token);

        timeProvider.Advance(TimeSpan.FromDays(31));
        using HttpResponseMessage expiredAccount = await Confirm(client, user.Id, token);

        await AssertInvalidProblem(malformed);
        await AssertInvalidProblem(altered);
        await AssertInvalidProblem(wrongUser);
        await AssertInvalidProblem(expiredAccount);
    }

    [Fact]
    public async Task ExpiredIdentityTokenReturnsTheGenericProblem()
    {
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(
            TimeProvider.System,
            TimeSpan.Zero);
        using HttpClient client = factory.CreateClient();
        MonKadoUser user = await RegisterAndLoadUser(factory, client, "token-expired@example.fr");
        string token = await GenerateEncodedToken(factory, user);

        using HttpResponseMessage response = await Confirm(client, user.Id, token);

        await AssertInvalidProblem(response);
    }

    [Fact]
    public async Task ConcurrentConfirmationsAreIdempotent()
    {
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(TimeProvider.System);
        using HttpClient registrationClient = factory.CreateClient();
        MonKadoUser user = await RegisterAndLoadUser(
            factory,
            registrationClient,
            "concurrent-confirm@example.fr");
        string token = await GenerateEncodedToken(factory, user);
        using HttpClient firstClient = factory.CreateClient();
        using HttpClient secondClient = factory.CreateClient();

        Task<HttpResponseMessage> firstRequest = Confirm(firstClient, user.Id, token);
        Task<HttpResponseMessage> secondRequest = Confirm(secondClient, user.Id, token);
        HttpResponseMessage[] responses = await Task.WhenAll(firstRequest, secondRequest);
        using HttpResponseMessage firstResponse = responses[0];
        using HttpResponseMessage secondResponse = responses[1];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NoContent, response.StatusCode));
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.True((await context.Users.AsNoTracking().SingleAsync(
            TestContext.Current.CancellationToken)).EmailConfirmed);
    }

    [Fact]
    public async Task ResendUsesPersistentSilentQuotasWithoutExtendingAccountExpiry()
    {
        MutableTimeProvider timeProvider = new(ReferenceTime);
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(timeProvider);
        using HttpClient client = factory.CreateClient();
        MonKadoUser user = await RegisterAndLoadUser(factory, client, "quota@example.fr");
        DateTimeOffset originalExpiry = user.UnconfirmedAccountExpiresAt!.Value;

        await MarkPendingOutboxProcessed(factory, ReferenceTime);
        for (int requestNumber = 2; requestNumber <= 5; requestNumber++)
        {
            timeProvider.Advance(TimeSpan.FromMinutes(1));
            using HttpResponseMessage response = await RequestConfirmation(client, " QUOTA@example.fr ");
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
            await MarkPendingOutboxProcessed(factory, timeProvider.GetUtcNow());
        }

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        using HttpResponseMessage quotaResponse = await RequestConfirmation(client, "quota@example.fr");
        Assert.Equal(HttpStatusCode.Accepted, quotaResponse.StatusCode);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(5, await context.AuthenticationEmailOutboxMessages.CountAsync(
            TestContext.Current.CancellationToken));
        Assert.Equal(originalExpiry, await context.Users
            .Where(candidate => candidate.Id == user.Id)
            .Select(candidate => candidate.UnconfirmedAccountExpiresAt!.Value)
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ConcurrentResendsCreateAtMostOneNewOutboxMessage()
    {
        MutableTimeProvider timeProvider = new(ReferenceTime);
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(timeProvider);
        using HttpClient registrationClient = factory.CreateClient();
        await RegisterAndLoadUser(factory, registrationClient, "concurrent-resend@example.fr");
        await MarkPendingOutboxProcessed(factory, ReferenceTime);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        using HttpClient firstClient = factory.CreateClient();
        using HttpClient secondClient = factory.CreateClient();

        Task<HttpResponseMessage> firstRequest = RequestConfirmation(
            firstClient,
            "concurrent-resend@example.fr");
        Task<HttpResponseMessage> secondRequest = RequestConfirmation(
            secondClient,
            "CONCURRENT-RESEND@example.fr");
        HttpResponseMessage[] responses = await Task.WhenAll(firstRequest, secondRequest);
        using HttpResponseMessage firstResponse = responses[0];
        using HttpResponseMessage secondResponse = responses[1];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            2,
            await context.AuthenticationEmailOutboxMessages.CountAsync(
                TestContext.Current.CancellationToken));
        Assert.Equal(
            1,
            await context.AuthenticationEmailOutboxMessages.CountAsync(
                message => message.ProcessedAt == null,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ResendIsIndistinguishableForUnknownConfirmedExpiredAndPendingAccounts()
    {
        MutableTimeProvider timeProvider = new(ReferenceTime);
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory(timeProvider);
        using HttpClient client = factory.CreateClient();
        MonKadoUser confirmed = await RegisterAndLoadUser(factory, client, "confirmed@example.fr");
        await RegisterAndLoadUser(factory, client, "expired@example.fr");
        timeProvider.Advance(TimeSpan.FromDays(31));
        await RegisterAndLoadUser(factory, client, "pending@example.fr");
        int initialMessageCount;

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
        {
            MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.Users.Where(user => user.Id == confirmed.Id).ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.EmailConfirmed, true)
                    .SetProperty(user => user.UnconfirmedAccountExpiresAt, (DateTimeOffset?)null),
                TestContext.Current.CancellationToken);
            initialMessageCount = await context.AuthenticationEmailOutboxMessages.CountAsync(
                TestContext.Current.CancellationToken);
        }

        using HttpResponseMessage unknownResponse = await RequestConfirmation(client, "unknown@example.fr");
        using HttpResponseMessage confirmedResponse = await RequestConfirmation(client, "confirmed@example.fr");
        using HttpResponseMessage expiredResponse = await RequestConfirmation(client, "expired@example.fr");
        using HttpResponseMessage pendingResponse = await RequestConfirmation(client, "pending@example.fr");

        Assert.All(
            new[] { unknownResponse, confirmedResponse, expiredResponse, pendingResponse },
            response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));

        await using AsyncServiceScope assertionScope = factory.Services.CreateAsyncScope();
        MonKadoDbContext assertionContext =
            assertionScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(initialMessageCount, await assertionContext.AuthenticationEmailOutboxMessages.CountAsync(
            TestContext.Current.CancellationToken));
    }

    private async Task<PostgreSqlApiFactory> CreateMigratedFactory(
        TimeProvider timeProvider,
        TimeSpan? tokenLifespan = null)
    {
        PostgreSqlApiFactory factory = new(
            fixture.Container.GetConnectionString(),
            timeProvider,
            tokenLifespan);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE public.users CASCADE;",
            TestContext.Current.CancellationToken);
        return factory;
    }

    private static async Task<MonKadoUser> RegisterAndLoadUser(
        PostgreSqlApiFactory factory,
        HttpClient client,
        string email)
    {
        using HttpResponseMessage response = await SendWithCsrf(
            client,
            "/api/v1/auth/registrations",
            new { email, password = Password, displayName = "Member" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        return await context.Users.AsNoTracking().SingleAsync(
            user => user.Email == email,
            TestContext.Current.CancellationToken);
    }

    private static async Task<string> GenerateEncodedToken(
        PostgreSqlApiFactory factory,
        MonKadoUser user)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        UserManager<MonKadoUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        MonKadoUser trackedUser = await userManager.FindByIdAsync(user.Id.ToString())
            ?? throw new InvalidOperationException("The test user was not persisted.");
        string rawToken = await userManager.GenerateEmailConfirmationTokenAsync(trackedUser);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(rawToken))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static Task<HttpResponseMessage> Confirm(
        HttpClient client,
        Guid userId,
        string token)
    {
        return SendWithCsrf(
            client,
            "/api/v1/auth/email-confirmations",
            new { userId = userId.ToString(), token });
    }

    private static Task<HttpResponseMessage> RequestConfirmation(
        HttpClient client,
        string email)
    {
        return SendWithCsrf(
            client,
            "/api/v1/auth/email-confirmation-requests",
            new { email });
    }

    private static async Task<HttpResponseMessage> SendWithCsrf(
        HttpClient client,
        string requestUri,
        object payload)
    {
        using HttpResponseMessage tokenResponse = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        CsrfTokenResponse tokenPayload =
            await tokenResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>(
                TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The CSRF token response is empty.");
        HttpRequestMessage request = new(HttpMethod.Post, requestUri)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, tokenPayload.Token);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task AssertInvalidProblem(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using JsonDocument payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            "EMAIL_CONFIRMATION_INVALID",
            payload.RootElement.GetProperty("code").GetString());
        Assert.Equal(
            "The email confirmation link is invalid or expired.",
            payload.RootElement.GetProperty("detail").GetString());
    }

    private static async Task MarkPendingOutboxProcessed(
        PostgreSqlApiFactory factory,
        DateTimeOffset processedAt)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.AuthenticationEmailOutboxMessages
            .Where(message => message.ProcessedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(message => message.ProcessedAt, processedAt),
                TestContext.Current.CancellationToken);
    }

    private sealed class MutableTimeProvider(DateTimeOffset currentTime) : TimeProvider
    {
        private DateTimeOffset current = currentTime;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration)
        {
            current = current.Add(duration);
        }
    }
}
