using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Errors;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class EmailConfirmationIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Password = "a sufficiently long password";
    private static readonly DateTimeOffset _referenceTime =
        new(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_WhenValidConfirmation_UpdatesAccountCancelsOutboxAndCanBeReplayed()
    {
        // Arrange
        var timeProvider = new MutableTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        using var client = factory.CreateClient();
        var user = await RegisterAndLoadUserAsync(
            factory,
            client,
            "valid@example.fr");
        var token = await GenerateEncodedTokenAsync(
            factory,
            user);

        using var response = await ConfirmAsync(
            client,
            user.Id,
            token);
        // Act
        using var replay = await ConfirmAsync(
            client,
            user.Id,
            token);

        // Assert
        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            replay.StatusCode);
        Assert.Equal(
            "no-store",
            response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("Set-Cookie"));

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var confirmed = await context.Users.AsNoTracking().SingleAsync(
            candidate => candidate.Id == user.Id,
            TestContext.Current.CancellationToken);
        var outbox = await context.AuthenticationEmailOutboxMessages
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);

        Assert.True(confirmed.EmailConfirmed);
        Assert.Null(confirmed.UnconfirmedAccountExpiresAt);
        Assert.Equal(
            _referenceTime.UtcDateTime,
            confirmed.UpdatedAt);
        Assert.NotEqual(
            user.Version,
            confirmed.Version);
        Assert.Equal(
            _referenceTime.UtcDateTime,
            outbox.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenInvalidAlteredWrongUserAndExpiredAccountReturnTheSameProblem_Completes()
    {
        // Arrange
        var timeProvider = new MutableTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        using var client = factory.CreateClient();
        var user = await RegisterAndLoadUserAsync(
            factory,
            client,
            "invalid@example.fr");
        var token = await GenerateEncodedTokenAsync(
            factory,
            user);

        using var malformed = await ConfirmAsync(
            client,
            user.Id,
            "***");
        using var altered = await ConfirmAsync(
            client,
            user.Id,
            token[..^1] + "A");
        using var wrongUser = await ConfirmAsync(
            client,
            Guid.CreateVersion7(),
            token);

        timeProvider.Advance(TimeSpan.FromDays(31));
        using var expiredAccount = await ConfirmAsync(
            client,
            user.Id,
            token);

        await AssertInvalidProblemAsync(malformed);
        await AssertInvalidProblemAsync(altered);
        await AssertInvalidProblemAsync(wrongUser);
        // Act
        await AssertInvalidProblemAsync(expiredAccount);
        // Assert
    }

    [Fact]
    public async Task ExecuteAsync_WhenExpiredIdentityToken_ReturnsTheGenericProblem()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(
            TimeProvider.System,
            TimeSpan.Zero);
        using var client = factory.CreateClient();
        var user = await RegisterAndLoadUserAsync(
            factory,
            client,
            "token-expired@example.fr");
        var token = await GenerateEncodedTokenAsync(
            factory,
            user);

        using var response = await ConfirmAsync(
            client,
            user.Id,
            token);

        // Act
        await AssertInvalidProblemAsync(response);
        // Assert
    }

    [Fact]
    public async Task ConfirmAsync_WhenUnconfirmedAccountHasNoExpiration_ReturnsFalse()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(TimeProvider.System);
        using var client = factory.CreateClient();
        var user = await RegisterAndLoadUserAsync(
            factory,
            client,
            "missing-expiration@example.fr");
        var token = await GenerateEncodedTokenAsync(
            factory,
            user);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Users
            .Where(candidate => candidate.Id == user.Id)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    candidate => candidate.UnconfirmedAccountExpiresAt,
                    (DateTime?)null),
                TestContext.Current.CancellationToken);
        var service = scope.ServiceProvider.GetRequiredService<IEmailConfirmationService>();

        // Act
        var result = await service.ConfirmAsync(
            user.Id.ToString("D"),
            token,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task ConfirmAsync_WhenIdentityCannotPersistConfirmation_RollsBackAndReturnsFalse()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(
            TimeProvider.System,
            configureServices: services =>
            {
                services.RemoveAll<UserManager<MonKadoUser>>();
                services.AddScoped<UserManager<MonKadoUser>, FailingEmailConfirmationUserManager>();
            });
        using var client = factory.CreateClient();
        var user = await RegisterAndLoadUserAsync(
            factory,
            client,
            "confirmation-failure@example.fr");
        var token = await GenerateEncodedTokenAsync(
            factory,
            user);
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IEmailConfirmationService>();

        // Act
        var result = await service.ConfirmAsync(
            user.Id.ToString("D"),
            token,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var persistedUser = await context.Users
            .AsNoTracking()
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.False(persistedUser.EmailConfirmed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConcurrentConfirmations_AreIdempotent()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(TimeProvider.System);
        using var registrationClient = factory.CreateClient();
        var user = await RegisterAndLoadUserAsync(
            factory,
            registrationClient,
            "concurrent-confirm@example.fr");
        var token = await GenerateEncodedTokenAsync(
            factory,
            user);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();

        var firstRequest = ConfirmAsync(
            firstClient,
            user.Id,
            token);
        var secondRequest = ConfirmAsync(
            secondClient,
            user.Id,
            token);
        // Act
        var responses = await Task.WhenAll(
            firstRequest,
            secondRequest);
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];

        // Assert
        Assert.All(
            responses,
            response => Assert.Equal(
                HttpStatusCode.NoContent,
                response.StatusCode));
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.True((await context.Users.AsNoTracking().SingleAsync(
            TestContext.Current.CancellationToken)).EmailConfirmed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTransientPostgreSqlServerFailure_ReturnsServiceUnavailable()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(TimeProvider.System);
        using var client = factory.CreateClient();
        var user = await RegisterAndLoadUserAsync(
            factory,
            client,
            "transient@example.fr");
        var token = await GenerateEncodedTokenAsync(
            factory,
            user);
        await CreateTransientUserUpdateTriggerAsync();
        try
        {
            // Act
            using var response = await ConfirmAsync(
                client,
                user.Id,
                token);

            // Assert
            Assert.Equal(
                HttpStatusCode.ServiceUnavailable,
                response.StatusCode);
            using var payload = await response.Content.ReadFromJsonAsync<JsonDocument>(
                TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException("The dependency problem response is empty.");
            Assert.Equal(
                ErrorCodes.TechnicalDependencyUnavailable,
                payload.RootElement.GetProperty("errorCode").GetString());
        }
        finally
        {
            await DropTransientUserUpdateTriggerAsync();
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenKnownAndUnknownResendsUseTheMinimumResponseDuration_Completes()
    {
        // Arrange
        var timeProvider = new MutableTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        using var client = factory.CreateClient();
        await RegisterAndLoadUserAsync(
            factory,
            client,
            "timing@example.fr");

        var unknownStopwatch =
            System.Diagnostics.Stopwatch.StartNew();
        using var unknownResponse = await RequestConfirmationAsync(
            client,
            "unknown-timing@example.fr");
        unknownStopwatch.Stop();

        var knownStopwatch =
            System.Diagnostics.Stopwatch.StartNew();
        // Act
        using var knownResponse = await RequestConfirmationAsync(
            client,
            "timing@example.fr");
        knownStopwatch.Stop();

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            unknownResponse.StatusCode);
        Assert.Equal(
            HttpStatusCode.Accepted,
            knownResponse.StatusCode);
        Assert.True(
            unknownStopwatch.Elapsed >= TimeSpan.FromMilliseconds(175),
            $"Unknown-account response completed in {unknownStopwatch.Elapsed.TotalMilliseconds} ms.");
        Assert.True(
            knownStopwatch.Elapsed >= TimeSpan.FromMilliseconds(175),
            $"Known-account response completed in {knownStopwatch.Elapsed.TotalMilliseconds} ms.");
    }

    [Fact]
    public async Task ExecuteAsync_WhenResend_UsesPersistentSilentQuotasWithoutExtendingAccountExpiry()
    {
        // Arrange
        var timeProvider = new MutableTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        using var client = factory.CreateClient();
        var user = await RegisterAndLoadUserAsync(
            factory,
            client,
            "quota@example.fr");
        var originalExpiry = user.UnconfirmedAccountExpiresAt!.Value;

        await MarkPendingOutboxProcessedAsync(
            factory,
            _referenceTime);
        for (var requestNumber = 2; requestNumber <= 5; requestNumber++)
        {
            timeProvider.Advance(TimeSpan.FromMinutes(1));
            using var response = await RequestConfirmationAsync(
                client,
                " QUOTA@example.fr ");
            Assert.Equal(
                HttpStatusCode.Accepted,
                response.StatusCode);
            Assert.Equal(
                "no-store",
                response.Headers.CacheControl?.ToString());
            await MarkPendingOutboxProcessedAsync(
                factory,
                timeProvider.GetUtcNow());
        }

        timeProvider.Advance(TimeSpan.FromMinutes(1));
        // Act
        using var quotaResponse = await RequestConfirmationAsync(
            client,
            "quota@example.fr");
        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            quotaResponse.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            5,
            await context.AuthenticationEmailOutboxMessages.CountAsync(
            TestContext.Current.CancellationToken));
        Assert.Equal(
            originalExpiry,
            await context.Users
            .Where(candidate => candidate.Id == user.Id)
            .Select(candidate => candidate.UnconfirmedAccountExpiresAt!.Value)
            .SingleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_WhenConcurrentResendsCreateAtMostOneNewOutboxMessage_Completes()
    {
        // Arrange
        var timeProvider = new MutableTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        using var registrationClient = factory.CreateClient();
        await RegisterAndLoadUserAsync(
            factory,
            registrationClient,
            "concurrent-resend@example.fr");
        await MarkPendingOutboxProcessedAsync(
            factory,
            _referenceTime);
        timeProvider.Advance(TimeSpan.FromMinutes(1));
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();

        var firstRequest = RequestConfirmationAsync(
            firstClient,
            "concurrent-resend@example.fr");
        var secondRequest = RequestConfirmationAsync(
            secondClient,
            "CONCURRENT-RESEND@example.fr");
        // Act
        var responses = await Task.WhenAll(
            firstRequest,
            secondRequest);
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];

        // Assert
        Assert.All(
            responses,
            response => Assert.Equal(
                HttpStatusCode.Accepted,
                response.StatusCode));
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
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
    public async Task ExecuteAsync_WhenResend_IsIndistinguishableForUnknownConfirmedExpiredAndPendingAccounts()
    {
        // Arrange
        var timeProvider = new MutableTimeProvider(_referenceTime);
        await using var factory = await CreateMigratedFactoryAsync(timeProvider);
        using var client = factory.CreateClient();
        var confirmed = await RegisterAndLoadUserAsync(
            factory,
            client,
            "confirmed@example.fr");
        await RegisterAndLoadUserAsync(
            factory,
            client,
            "expired@example.fr");
        timeProvider.Advance(TimeSpan.FromDays(31));
        await RegisterAndLoadUserAsync(
            factory,
            client,
            "pending@example.fr");
        int initialMessageCount;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await context.Users.Where(user => user.Id == confirmed.Id).ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        user => user.EmailConfirmed,
                        true)
                    .SetProperty(
                        user => user.UnconfirmedAccountExpiresAt,
                        (DateTime?)null),
                TestContext.Current.CancellationToken);
            initialMessageCount = await context.AuthenticationEmailOutboxMessages.CountAsync(
                TestContext.Current.CancellationToken);
        }

        using var unknownResponse = await RequestConfirmationAsync(
            client,
            "unknown@example.fr");
        using var confirmedResponse = await RequestConfirmationAsync(
            client,
            "confirmed@example.fr");
        using var expiredResponse = await RequestConfirmationAsync(
            client,
            "expired@example.fr");
        // Act
        using var pendingResponse = await RequestConfirmationAsync(
            client,
            "pending@example.fr");

        // Assert
        Assert.All(
            [
                unknownResponse,
                confirmedResponse,
                expiredResponse,
                pendingResponse
            ],
            response => Assert.Equal(
                HttpStatusCode.Accepted,
                response.StatusCode));

        await using var assertionScope = factory.Services.CreateAsyncScope();
        var assertionContext =
            assertionScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Equal(
            initialMessageCount,
            await assertionContext.AuthenticationEmailOutboxMessages.CountAsync(
            TestContext.Current.CancellationToken));
    }

    private async Task<PostgreSqlApiFactory> CreateMigratedFactoryAsync(
        TimeProvider timeProvider,
        TimeSpan? tokenLifespan = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            timeProvider,
            tokenLifespan,
            configureServices);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE public.users CASCADE;",
            TestContext.Current.CancellationToken);

        return factory;
    }

    private static async Task<MonKadoUser> RegisterAndLoadUserAsync(
        PostgreSqlApiFactory factory,
        HttpClient client,
        string email)
    {
        using var response = await SendWithCsrfAsync(
            client,
            "/api/v1/auth/registrations",
            new
            {
                email,
                password = Password,
                displayName = "Member"
            });
        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();

        return await context.Users.AsNoTracking().SingleAsync(
            user => user.Email == email,
            TestContext.Current.CancellationToken);
    }

    private static async Task<string> GenerateEncodedTokenAsync(
        PostgreSqlApiFactory factory,
        MonKadoUser user)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var trackedUser = await userManager.FindByIdAsync(user.Id.ToString())
            ?? throw new InvalidOperationException("The test user was not persisted.");
        var rawToken = await userManager.GenerateEmailConfirmationTokenAsync(trackedUser);

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(rawToken))
            .TrimEnd('=')
            .Replace(
                '+',
                '-')
            .Replace(
                '/',
                '_');
    }

    private static Task<HttpResponseMessage> ConfirmAsync(
        HttpClient client,
        Guid userId,
        string token)
    {

        return SendWithCsrfAsync(
            client,
            "/api/v1/auth/email-confirmations",
            new
            {
                userId = userId.ToString(),
                token
            });
    }

    private static Task<HttpResponseMessage> RequestConfirmationAsync(
        HttpClient client,
        string email)
    {

        return SendWithCsrfAsync(
            client,
            "/api/v1/auth/email-confirmation-requests",
            new
            {
                email
            });
    }

    private static async Task<HttpResponseMessage> SendWithCsrfAsync(
        HttpClient client,
        string requestUri,
        object payload)
    {
        using var tokenResponse = await client.GetAsync(
            "/security/csrf-token",
            TestContext.Current.CancellationToken);
        var tokenPayload =
            await tokenResponse.Content.ReadFromJsonAsync<CsrfTokenResponse>(
                TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException("The CSRF token response is empty.");
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            requestUri)
        {
            Content = JsonContent.Create(payload)
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            tokenPayload.Token);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task AssertInvalidProblemAsync(HttpResponseMessage response)
    {
        Assert.Equal(
            HttpStatusCode.BadRequest,
            response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        using var payload = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            ErrorCodes.AccountEmailConfirmationInvalid,
            payload.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal(
            "The email confirmation link is invalid or expired.",
            payload.RootElement.GetProperty("message").GetString());
    }

    private static async Task MarkPendingOutboxProcessedAsync(
        PostgreSqlApiFactory factory,
        DateTimeOffset processedAt)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.AuthenticationEmailOutboxMessages
            .Where(message => message.ProcessedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.ProcessedAt,
                    processedAt.UtcDateTime),
                TestContext.Current.CancellationToken);
    }

    private async Task CreateTransientUserUpdateTriggerAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE OR REPLACE FUNCTION public.raise_transient_confirmation_failure()
            RETURNS trigger
            LANGUAGE plpgsql
            AS $$
            BEGIN
                RAISE EXCEPTION 'temporary database outage' USING ERRCODE = '57P03';
            END;
            $$;

            CREATE TRIGGER reject_confirmation_update
            BEFORE UPDATE ON public.users
            FOR EACH ROW
            EXECUTE FUNCTION public.raise_transient_confirmation_failure();
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task DropTransientUserUpdateTriggerAsync()
    {
        await using var connection = new Npgsql.NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            DROP TRIGGER IF EXISTS reject_confirmation_update ON public.users;
            DROP FUNCTION IF EXISTS public.raise_transient_confirmation_failure();
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

}
