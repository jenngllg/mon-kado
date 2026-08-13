using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using JennGllg.Fr.MonKado.Back.Api.Security;
using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public sealed class AccountRegistrationIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Password = "  a long secure password  ";

    [Fact]
    public async Task RegistrationPersistsOneHashedAccountAndOneMinimalOutboxMessage()
    {
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory();
        using HttpClient client = factory.CreateClient();
        DateTimeOffset beforeRegistration = DateTimeOffset.UtcNow;

        using HttpResponseMessage response = await Register(
            client,
            " Lea@example.fr ",
            Password,
            " Lea ");

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        MonKadoUser user = await context.Users.SingleAsync(TestContext.Current.CancellationToken);
        AuthenticationEmailOutboxMessage message = await context.AuthenticationEmailOutboxMessages
            .SingleAsync(TestContext.Current.CancellationToken);
        IPasswordHasher<MonKadoUser> passwordHasher =
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<MonKadoUser>>();

        Assert.Equal(7, user.Id.Version);
        Assert.Equal("Lea@example.fr", user.Email);
        Assert.Equal("LEA@EXAMPLE.FR", user.NormalizedEmail);
        Assert.Equal("Lea", user.DisplayName);
        Assert.False(user.EmailConfirmed);
        Assert.InRange(user.UnconfirmedAccountExpiresAt!.Value,
            beforeRegistration.AddDays(30), DateTimeOffset.UtcNow.AddDays(30));
        Assert.NotNull(user.PasswordHash);
        Assert.DoesNotContain(Password, user.PasswordHash, StringComparison.Ordinal);
        byte[] decodedHash = Convert.FromBase64String(user.PasswordHash);
        Assert.Equal(220_000u, BinaryPrimitives.ReadUInt32BigEndian(decodedHash.AsSpan(5, 4)));

        Assert.Equal(
            PasswordVerificationResult.Success,
            passwordHasher.VerifyHashedPassword(user, user.PasswordHash, Password));
        Assert.Equal(user.Id, message.UserId);
        Assert.Equal(AuthenticationEmailKind.EmailConfirmation, message.Kind);
        Assert.Equal(0, message.AttemptCount);
        Assert.Null(message.ProcessedAt);
        Assert.Null(message.LastError);

        await using NpgsqlConnection connection = new(fixture.Container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT kind FROM public.authentication_email_outbox;";
        Assert.Equal(
            "EMAIL_CONFIRMATION",
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task IdentityStoreRejectsPasswordsLongerThanThePublicContract()
    {
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory();
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        UserManager<MonKadoUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        MonKadoUser user = new()
        {
            Id = Guid.CreateVersion7(),
            Email = "defense-in-depth@example.fr",
            UserName = "defense-in-depth@example.fr",
            DisplayName = "Defense in depth",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            UnconfirmedAccountExpiresAt = DateTimeOffset.UtcNow.AddDays(30)
        };

        IdentityResult result = await userManager.CreateAsync(user, new string('a', 129));

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, error => error.Code == "PasswordTooLong");
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.Users.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExistingAccountReturnsSameResponseWithoutChangingItOrAddingOutboxMessage()
    {
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory();
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage firstResponse = await Register(
            client, "lea@example.fr", Password, "Lea");
        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);

        using HttpResponseMessage secondResponse = await Register(
            client, "LEA@example.fr", "a different password", "Changed");

        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);
        Assert.Equal(firstResponse.Content.Headers.ContentLength, secondResponse.Content.Headers.ContentLength);
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        MonKadoUser user = await context.Users.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal("Lea", user.DisplayName);
        Assert.Single(await context.AuthenticationEmailOutboxMessages.ToArrayAsync(
            TestContext.Current.CancellationToken));
        IPasswordHasher<MonKadoUser> passwordHasher =
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<MonKadoUser>>();
        Assert.Equal(
            PasswordVerificationResult.Success,
            passwordHasher.VerifyHashedPassword(user, user.PasswordHash!, Password));
    }

    [Fact]
    public async Task ConcurrentRegistrationsCreateOnlyOneAccountAndOutboxMessage()
    {
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory();
        using HttpClient firstClient = factory.CreateClient();
        using HttpClient secondClient = factory.CreateClient();

        Task<HttpResponseMessage> firstRequest = Register(
            firstClient, "concurrent@example.fr", Password, "First");
        Task<HttpResponseMessage> secondRequest = Register(
            secondClient, "CONCURRENT@example.fr", Password, "Second");
        HttpResponseMessage[] responses = await Task.WhenAll(firstRequest, secondRequest);
        using HttpResponseMessage firstResponse = responses[0];
        using HttpResponseMessage secondResponse = responses[1];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Single(await context.Users.ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Single(await context.AuthenticationEmailOutboxMessages.ToArrayAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OutboxFailureRollsBackAccountCreation()
    {
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory();
        await CreateRejectingOutboxTrigger();
        try
        {
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await Register(
                client, "rollback@example.fr", Password, "Rollback");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
            MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            Assert.Empty(await context.Users.ToArrayAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await context.AuthenticationEmailOutboxMessages.ToArrayAsync(
                TestContext.Current.CancellationToken));
        }
        finally
        {
            await DropRejectingOutboxTrigger();
        }
    }

    [Fact]
    public async Task CleanupDeletesOnlyExpiredUnconfirmedAccountsAndCascadesOutbox()
    {
        await using PostgreSqlApiFactory factory = await CreateMigratedFactory();
        using HttpClient client = factory.CreateClient();
        await RegisterAndDispose(client, "expired@example.fr", "Expired");
        await RegisterAndDispose(client, "valid@example.fr", "Valid");
        await RegisterAndDispose(client, "confirmed@example.fr", "Confirmed");

        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        MonKadoUser expired = await context.Users.SingleAsync(
            user => user.NormalizedEmail == "EXPIRED@EXAMPLE.FR",
            TestContext.Current.CancellationToken);
        MonKadoUser confirmed = await context.Users.SingleAsync(
            user => user.NormalizedEmail == "CONFIRMED@EXAMPLE.FR",
            TestContext.Current.CancellationToken);
        DateTimeOffset cleanupCutoff = DateTimeOffset.UtcNow.AddDays(1);
        expired.UnconfirmedAccountExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
        confirmed.EmailConfirmed = true;
        confirmed.UnconfirmedAccountExpiresAt = DateTimeOffset.UtcNow.AddHours(1);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        IExpiredAccountCleanup cleanup = scope.ServiceProvider.GetRequiredService<IExpiredAccountCleanup>();
        int deletedCount = await cleanup.DeleteExpiredUnconfirmedAccountsAsync(
            cleanupCutoff,
            500,
            TestContext.Current.CancellationToken);

        Assert.Equal(1, deletedCount);
        string[] remainingEmails = await context.Users
            .OrderBy(user => user.NormalizedEmail)
            .Select(user => user.NormalizedEmail!)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["CONFIRMED@EXAMPLE.FR", "VALID@EXAMPLE.FR"], remainingEmails);
        Assert.Equal(2, await context.AuthenticationEmailOutboxMessages.CountAsync(
            TestContext.Current.CancellationToken));
    }

    private async Task<PostgreSqlApiFactory> CreateMigratedFactory()
    {
        PostgreSqlApiFactory factory = new(fixture.Container.GetConnectionString());
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE public.users CASCADE;",
            TestContext.Current.CancellationToken);
        return factory;
    }

    private static async Task<HttpResponseMessage> Register(
        HttpClient client,
        string email,
        string password,
        string displayName)
    {
        string csrfToken = await GetCsrfToken(client);
        HttpRequestMessage request = new(HttpMethod.Post, "/api/v1/auth/registrations")
        {
            Content = JsonContent.Create(new { email, password, displayName })
        };
        request.Headers.Add(WebSecurityOptions.AntiforgeryHeaderName, csrfToken);
        return await client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    private static async Task RegisterAndDispose(HttpClient client, string email, string displayName)
    {
        using HttpResponseMessage response = await Register(client, email, Password, displayName);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
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

    private async Task CreateRejectingOutboxTrigger()
    {
        await using NpgsqlConnection connection = new(fixture.Container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE OR REPLACE FUNCTION public.reject_authentication_email_outbox()
            RETURNS trigger AS $$
            BEGIN
                RAISE EXCEPTION 'outbox rejected by integration test';
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER reject_authentication_email_outbox
            BEFORE INSERT ON public.authentication_email_outbox
            FOR EACH ROW EXECUTE FUNCTION public.reject_authentication_email_outbox();
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task DropRejectingOutboxTrigger()
    {
        await using NpgsqlConnection connection = new(fixture.Container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            """
            DROP TRIGGER IF EXISTS reject_authentication_email_outbox
                ON public.authentication_email_outbox;
            DROP FUNCTION IF EXISTS public.reject_authentication_email_outbox();
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
