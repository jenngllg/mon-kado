using JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;
using JennGllg.Fr.MonKado.Back.Api.Options;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
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

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Npgsql;

using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

[Collection(PostgreSqlApiTestSuite.Name)]
public class AccountRegistrationIntegrationTests(PostgreSqlContainerFixture fixture)
{
    private const string Password = "  a long secure password  ";

    [Fact]
    public async Task RegisterAsync_WhenRegistrationPersistsOneHashedAccountAndOneMinimalOutboxMessage_Completes()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        using var client = factory.CreateClient();
        var beforeRegistration = DateTime.UtcNow;

        // Act
        using var response = await RegisterAsync(
            client,
            " Lea@example.fr ",
            Password,
            " Lea ");

        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var user = await context.Users.SingleAsync(TestContext.Current.CancellationToken);
        var message = await context.AuthenticationEmailOutboxMessages
            .SingleAsync(TestContext.Current.CancellationToken);
        var passwordHasher =
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<MonKadoUser>>();

        Assert.Equal(
            7,
            user.Id.Version);
        Assert.Equal(
            "Lea@example.fr",
            user.Email);
        Assert.Equal(
            "LEA@EXAMPLE.FR",
            user.NormalizedEmail);
        Assert.Equal(
            "Lea",
            user.DisplayName);
        Assert.False(user.EmailConfirmed);
        Assert.InRange(
            user.UnconfirmedAccountExpiresAt!.Value,
            beforeRegistration.AddDays(30),
            DateTime.UtcNow.AddDays(30));
        Assert.NotNull(user.PasswordHash);
        Assert.DoesNotContain(
            Password,
            user.PasswordHash,
            StringComparison.Ordinal);
        var decodedHash = Convert.FromBase64String(user.PasswordHash);
        Assert.Equal(
            220_000u,
            BinaryPrimitives.ReadUInt32BigEndian(decodedHash.AsSpan(
                5,
                4)));

        Assert.Equal(
            PasswordVerificationResult.Success,
            passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash,
                Password));
        Assert.Equal(
            user.Id,
            message.UserId);
        Assert.Equal(
            AuthenticationEmailKind.EmailConfirmation,
            message.Kind);
        Assert.Equal(
            0,
            message.AttemptCount);
        Assert.Null(message.ProcessedAt);
        Assert.Null(message.LastError);
        var roles = await context.UserRoles
            .Where(userRole => userRole.UserId == user.Id)
            .Join(
                context.Roles,
                userRole => userRole.RoleId,
                role => role.Id,
                (
                    _,
                    role) => role.Name ?? string.Empty)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [RoleNames.Member],
            roles);

        await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT kind FROM public.authentication_email_outbox;";
        Assert.Equal(
            "EMAIL_CONFIRMATION",
            await command.ExecuteScalarAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterAsync_WhenIdentityStore_RejectsPasswordsLongerThanThePublicContract()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = new MonKadoUser()
        {
            Id = Guid.CreateVersion7(),
            Email = "defense-in-depth@example.fr",
            UserName = "defense-in-depth@example.fr",
            DisplayName = "Defense in depth",
            UnconfirmedAccountExpiresAt = DateTime.UtcNow.AddDays(30)
        };

        // Act
        var result = await userManager.CreateAsync(
            user,
            new string(
                'a',
                129));

        // Assert
        Assert.False(result.Succeeded);
        Assert.Contains(
            result.Errors,
            error => error.Code == "PasswordTooLong");
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.Users.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterAsync_WhenIdentityRejectsAccountCreation_ThrowsDetailedException()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountRegistrationService>();
        var password = new string(
            'a',
            129);

        // Act
        Task action() => service.RegisterAsync(
            "rejected@example.fr",
            password,
            "Rejected",
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>((Func<Task>)action);
        Assert.Contains(
            "PasswordTooLong",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DuplicateEmail")]
    [InlineData("DuplicateUserName")]
    public async Task RegisterAsync_WhenIdentityReportsDuplicateAccount_RollsBackSilently(
        string errorCode)
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync(services =>
        {
            services.RemoveAll<IPasswordValidator<MonKadoUser>>();
            services.AddSingleton<IPasswordValidator<MonKadoUser>>(
                new RejectingPasswordValidator(errorCode));
        });
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IAccountRegistrationService>();

        // Act
        await service.RegisterAsync(
            $"{errorCode}@example.fr",
            Password,
            "Duplicate",
            TestContext.Current.CancellationToken);

        // Assert
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        Assert.Empty(await context.Users.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RegisterAsync_WhenDatabaseReportsNormalizedAccountConflict_CompletesSilently(
        bool isUserNameConstraint)
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        await CreateDuplicateAccountTriggerAsync(isUserNameConstraint);

        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<IAccountRegistrationService>();

            // Act
            await service.RegisterAsync(
                "database-duplicate@example.fr",
                Password,
                "Database duplicate",
                TestContext.Current.CancellationToken);

            // Assert
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            Assert.Empty(await context.Users.ToArrayAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            await DropDuplicateAccountTriggerAsync();
        }
    }

    [Fact]
    public async Task RegisterAsync_WhenExistingAccount_ReturnsSameResponseWithoutChangingItOrAddingOutboxMessage()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        using var client = factory.CreateClient();
        // Act
        using var firstResponse = await RegisterAsync(
            client,
            "lea@example.fr",
            Password,
            "Lea");
        // Assert
        Assert.Equal(
            HttpStatusCode.Accepted,
            firstResponse.StatusCode);

        using var secondResponse = await RegisterAsync(
            client,
            "LEA@example.fr",
            "a different password",
            "Changed");

        Assert.Equal(
            HttpStatusCode.Accepted,
            secondResponse.StatusCode);
        Assert.Equal(
            firstResponse.Content.Headers.ContentLength,
            secondResponse.Content.Headers.ContentLength);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var user = await context.Users.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            "Lea",
            user.DisplayName);
        Assert.Single(await context.AuthenticationEmailOutboxMessages.ToArrayAsync(
            TestContext.Current.CancellationToken));
        var passwordHasher =
            scope.ServiceProvider.GetRequiredService<IPasswordHasher<MonKadoUser>>();
        Assert.Equal(
            PasswordVerificationResult.Success,
            passwordHasher.VerifyHashedPassword(
                user,
                user.PasswordHash!,
                Password));
    }

    [Fact]
    public async Task RegisterAsync_WhenConcurrentRegistrationsCreateOnlyOneAccountAndOutboxMessage_Completes()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();

        var firstRequest = RegisterAsync(
            firstClient,
            "concurrent@example.fr",
            Password,
            "First");
        var secondRequest = RegisterAsync(
            secondClient,
            "CONCURRENT@example.fr",
            Password,
            "Second");
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
        Assert.Single(await context.Users.ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Single(await context.AuthenticationEmailOutboxMessages.ToArrayAsync(
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RegisterAsync_WhenOutboxFailureRollsBackAccountCreation_Completes()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        await CreateRejectingOutboxTriggerAsync();
        try
        {
            // Act
            using var client = factory.CreateClient();
            using var response = await RegisterAsync(
                client,
                "rollback@example.fr",
                Password,
                "Rollback");

            // Assert
            Assert.Equal(
                HttpStatusCode.InternalServerError,
                response.StatusCode);
            await using var scope = factory.Services.CreateAsyncScope();
            var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            Assert.Empty(await context.Users.ToArrayAsync(TestContext.Current.CancellationToken));
            Assert.Empty(await context.AuthenticationEmailOutboxMessages.ToArrayAsync(
                TestContext.Current.CancellationToken));
        }
        finally
        {
            await DropRejectingOutboxTriggerAsync();
        }
    }

    [Fact]
    public async Task RegisterAsync_WhenCleanup_DeletesOnlyExpiredUnconfirmedAccountsAndCascadesOutbox()
    {
        // Arrange
        await using var factory = await CreateMigratedFactoryAsync();
        using var client = factory.CreateClient();
        await RegisterAndDisposeAsync(
            client,
            "expired@example.fr",
            "Expired");
        await RegisterAndDisposeAsync(
            client,
            "valid@example.fr",
            "Valid");
        await RegisterAndDisposeAsync(
            client,
            "confirmed@example.fr",
            "Confirmed");

        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var expired = await context.Users.SingleAsync(
            user => user.NormalizedEmail == "EXPIRED@EXAMPLE.FR",
            TestContext.Current.CancellationToken);
        var confirmed = await context.Users.SingleAsync(
            user => user.NormalizedEmail == "CONFIRMED@EXAMPLE.FR",
            TestContext.Current.CancellationToken);
        var cleanupCutoff = DateTime.UtcNow.AddDays(1);
        expired.UnconfirmedAccountExpiresAt = DateTime.UtcNow.AddHours(1);
        confirmed.EmailConfirmed = true;
        confirmed.UnconfirmedAccountExpiresAt = DateTime.UtcNow.AddHours(1);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var cleanup = scope.ServiceProvider.GetRequiredService<IExpiredAccountCleanup>();
        // Act
        var deletedCount = await cleanup.DeleteExpiredUnconfirmedAccountsAsync(
            cleanupCutoff,
            500,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            1,
            deletedCount);
        Assert.Equal(
            0,
            await cleanup.DeleteExpiredUnconfirmedAccountsAsync(
                cleanupCutoff,
                500,
                TestContext.Current.CancellationToken));
        var remainingEmails = await context.Users
            .OrderBy(user => user.NormalizedEmail)
            .Select(user => user.NormalizedEmail!)
            .ToArrayAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            [
                "CONFIRMED@EXAMPLE.FR",
                "VALID@EXAMPLE.FR"
            ],
            remainingEmails);
        Assert.Equal(
            2,
            await context.AuthenticationEmailOutboxMessages.CountAsync(
            TestContext.Current.CancellationToken));
    }

    private async Task<PostgreSqlApiFactory> CreateMigratedFactoryAsync(
        Action<IServiceCollection>? configureServices = null)
    {
        var factory = new PostgreSqlApiFactory(
            fixture.Container.GetConnectionString(),
            configureServices: configureServices);
        await using var scope = factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        await context.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE public.users CASCADE;",
            TestContext.Current.CancellationToken);

        return factory;
    }

    private static async Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string email,
        string password,
        string displayName)
    {
        var csrfToken = await GetCsrfTokenAsync(client);
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/registrations")
        {
            Content = JsonContent.Create(new
            {
                email,
                password,
                displayName
            })
        };
        request.Headers.Add(
            WebSecurityOptions.AntiforgeryHeaderName,
            csrfToken);

        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    private static async Task RegisterAndDisposeAsync(
        HttpClient client,
        string email,
        string displayName)
    {
        using var response = await RegisterAsync(
            client,
            email,
            Password,
            displayName);
        Assert.Equal(
            HttpStatusCode.Accepted,
            response.StatusCode);
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

    private async Task CreateRejectingOutboxTriggerAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
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

    private async Task DropRejectingOutboxTriggerAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DROP TRIGGER IF EXISTS reject_authentication_email_outbox
                ON public.authentication_email_outbox;
            DROP FUNCTION IF EXISTS public.reject_authentication_email_outbox();
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task CreateDuplicateAccountTriggerAsync(bool isUserNameConstraint)
    {
        await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = isUserNameConstraint
            ?
            """
            CREATE OR REPLACE FUNCTION public.reject_duplicate_account()
            RETURNS trigger AS $$
            BEGIN
                RAISE unique_violation USING CONSTRAINT = 'ux_users_normalized_user_name';
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER reject_duplicate_account
            BEFORE INSERT ON public.users
            FOR EACH ROW EXECUTE FUNCTION public.reject_duplicate_account();
            """
            :
            """
            CREATE OR REPLACE FUNCTION public.reject_duplicate_account()
            RETURNS trigger AS $$
            BEGIN
                RAISE unique_violation USING CONSTRAINT = 'ux_users_normalized_email';
            END;
            $$ LANGUAGE plpgsql;
            CREATE TRIGGER reject_duplicate_account
            BEFORE INSERT ON public.users
            FOR EACH ROW EXECUTE FUNCTION public.reject_duplicate_account();
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }

    private async Task DropDuplicateAccountTriggerAsync()
    {
        await using var connection = new NpgsqlConnection(fixture.Container.GetConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            DROP TRIGGER IF EXISTS reject_duplicate_account ON public.users;
            DROP FUNCTION IF EXISTS public.reject_duplicate_account();
            """;
        await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
    }
}
