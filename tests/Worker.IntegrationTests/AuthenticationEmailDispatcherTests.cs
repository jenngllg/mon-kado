using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using System.Collections.Concurrent;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Worker.IntegrationTests;

[Collection(PostgreSqlWorkerTestSuite.Name)]
public class AuthenticationEmailDispatcherTests(PostgreSqlWorkerFixture fixture) : IDisposable
{
    private readonly string _keysPath = Path.Combine(
        Path.GetTempPath(),
        "mon-kado-worker-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExecuteAsync_WhenAcceptedMessage_IsProcessedAndProviderIdentifierIsStored()
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        var messageId = await CreateUnconfirmedAccountAsync(
            provider,
            now);

        await DispatchAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        // Act
        var message = await context.AuthenticationEmailOutboxMessages
            .SingleAsync(TestContext.Current.CancellationToken);
        // Assert
        Assert.Equal(
            messageId,
            sender.Messages.Single().OutboxMessageId);
        Assert.Equal(
            "fake-provider-id",
            message.ProviderMessageId);
        Assert.Equal(
            now.UtcDateTime,
            message.ProcessedAt);
        Assert.Null(message.LockedUntil);
        Assert.Null(message.LastError);
    }

    [Fact]
    public async Task ExecuteAsync_WhenTransientFailureReleasesLeaseAndSchedulesFirstRetry_Completes()
    {
        // Arrange
        var sender = new FakeEmailSender(fail: true);
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);

        await DispatchAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        // Act
        var message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        // Assert
        Assert.Equal(
            1,
            message.AttemptCount);
        Assert.Equal(
            now.UtcDateTime.AddMinutes(1),
            message.AvailableAt);
        Assert.Equal(
            "TRANSIENT",
            message.LastError);
        Assert.Null(message.LockedUntil);
        Assert.Null(message.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenProviderRetryAfterOverridesShorterConfiguredDelay_Completes()
    {
        // Arrange
        var sender = new FakeEmailSender(
            fail: true,
            retryAfter: TimeSpan.FromHours(2));
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);

        await DispatchAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        // Act
        var message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        // Assert
        Assert.Equal(
            now.AddHours(2),
            message.AvailableAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenAuthenticationFailure_UsesSlowRetryImmediately()
    {
        // Arrange
        var sender = new FakeEmailSender(
            fail: true,
            failureCategory: AuthenticationEmailFailureCategory.Authentication);
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);

        await DispatchAsync(provider);

        await using var scope = provider.CreateAsyncScope();
        // Act
        var message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        // Assert
        Assert.Equal(
            now.AddHours(6),
            message.AvailableAt);
        Assert.Equal(
            "AUTHENTICATION",
            message.LastError);
    }

    [Theory]
    [InlineData(2, AuthenticationEmailFailureCategory.Transient, null, 5)]
    [InlineData(3, AuthenticationEmailFailureCategory.Transient, 1, 15)]
    [InlineData(4, AuthenticationEmailFailureCategory.Transient, null, 60)]
    [InlineData(5, AuthenticationEmailFailureCategory.Transient, null, 360)]
    [InlineData(1, AuthenticationEmailFailureCategory.RateLimited, null, 1)]
    [InlineData(1, AuthenticationEmailFailureCategory.Permission, null, 360)]
    [InlineData(1, AuthenticationEmailFailureCategory.InvalidRequest, null, 360)]
    [InlineData(1, AuthenticationEmailFailureCategory.Unknown, null, 360)]
    [InlineData(1, AuthenticationEmailFailureCategory.Transient, 1_500, 1_440)]
    public async Task ExecuteAsync_WhenDeliveryFails_UsesExpectedRetryDelay(
        int attemptCount,
        AuthenticationEmailFailureCategory failureCategory,
        int? providerRetryMinutes,
        int expectedDelayMinutes)
    {
        // Arrange
        var sender = new FakeEmailSender(
            fail: true,
            retryAfter: providerRetryMinutes is null
                ? null
                : TimeSpan.FromMinutes(providerRetryMinutes.Value),
            failureCategory: failureCategory);
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupContext = setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await setupContext.AuthenticationEmailOutboxMessages.ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    message => message.AttemptCount,
                    attemptCount - 1),
                TestContext.Current.CancellationToken);
        }

        // Act
        await DispatchAsync(provider);

        // Assert
        await using var scope = provider.CreateAsyncScope();
        var message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            attemptCount,
            message.AttemptCount);
        Assert.Equal(
            now.AddMinutes(expectedDelayMinutes),
            message.AvailableAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenExpiredLease_CanBeClaimedAgain()
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupContext =
                setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await setupContext.AuthenticationEmailOutboxMessages.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(
                        message => message.LockedUntil,
                        now.UtcDateTime.AddMinutes(-1))
                    .SetProperty(
                        message => message.AttemptCount,
                        1),
                TestContext.Current.CancellationToken);
        }

        // Act
        await DispatchAsync(provider);

        // Assert
        Assert.Single(sender.Messages);
        await using var assertionScope = provider.CreateAsyncScope();
        var message = await assertionScope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            2,
            message.AttemptCount);
        Assert.NotNull(message.ProcessedAt);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConcurrentDispatchersClaimMessageOnlyOnce_Completes()
    {
        // Arrange
        var sender = new FakeEmailSender(delay: TimeSpan.FromMilliseconds(100));
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);

        // Act
        await Task.WhenAll(
            DispatchAsync(provider),
            DispatchAsync(provider));

        // Assert
        Assert.Single(sender.Messages);
    }

    [Theory]
    [InlineData("confirmed")]
    [InlineData("expired")]
    [InlineData("missing-account")]
    public async Task ExecuteAsync_WhenAccountCannotReceiveConfirmation_IsNotContactedAndMessageIsClosed(
        string scenario)
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);
        await using (var setupScope = provider.CreateAsyncScope())
        {
            var setupContext =
                setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            switch (scenario)
            {
                case "confirmed":
                    await setupContext.Users.ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            user => user.EmailConfirmed,
                            true),
                        TestContext.Current.CancellationToken);
                    break;
                case "expired":
                    await setupContext.Users.ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            user => user.UnconfirmedAccountExpiresAt,
                            now.UtcDateTime),
                        TestContext.Current.CancellationToken);
                    break;
                case "missing-account":
                    await setupContext.Database.OpenConnectionAsync(
                        TestContext.Current.CancellationToken);

                    try
                    {
                        await setupContext.Database.ExecuteSqlRawAsync(
                            "SET session_replication_role = replica;",
                            TestContext.Current.CancellationToken);
                        await setupContext.Users.ExecuteDeleteAsync(
                            TestContext.Current.CancellationToken);
                    }
                    finally
                    {
                        await setupContext.Database.ExecuteSqlRawAsync(
                            "SET session_replication_role = origin;",
                            TestContext.Current.CancellationToken);
                        await setupContext.Database.CloseConnectionAsync();
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unknown test scenario '{scenario}'.");
            }
        }

        // Act
        await DispatchAsync(provider);

        // Assert
        Assert.Empty(sender.Messages);
        await using var assertionScope = provider.CreateAsyncScope();
        var message = await assertionScope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(
            now.UtcDateTime,
            message.ProcessedAt);
    }

    [Theory]
    [InlineData("missing-message")]
    [InlineData("processed")]
    [InlineData("missing-lease")]
    [InlineData("expired-lease")]
    public async Task ExecuteAsync_WhenClaimedMessageBecomesUndeliverable_DoesNotContactProvider(
        string scenario)
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            provider,
            now);
        await CreateClaimMutationTriggerAsync(
            provider,
            scenario);

        // Act
        var claimedCount = await DispatchOneAsync(provider);

        // Assert
        Assert.Equal(
            1,
            claimedCount);
        Assert.Empty(sender.Messages);
    }

    [Fact]
    public async Task ExecuteAsync_WhenSeparateInstanceSharingKeys_CanValidateGeneratedToken()
    {
        // Arrange
        var sender = new FakeEmailSender();
        var now = new DateTimeOffset(
            2026,
            8,
            13,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var sendingProvider = await CreateProviderAsync(
            sender,
            now);
        await CreateUnconfirmedAccountAsync(
            sendingProvider,
            now);
        await DispatchAsync(sendingProvider);

        await using var validatingProvider = BuildProvider(
            new FakeEmailSender(),
            now);
        var delivery = sender.Messages.Single();
        var fragment = delivery.ConfirmationUrl.Fragment
            .TrimStart('#')
            .Split(
                '&',
                StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split(
                '=',
                2))
            .ToDictionary(
                parts => parts[0],
                parts => parts[1],
                StringComparer.Ordinal);
        var userId = Guid.Parse(fragment["userId"]);
        var token = DecodeBase64Url(fragment["token"]);

        // Act
        await using var scope = validatingProvider.CreateAsyncScope();
        var userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = (await userManager.FindByIdAsync(userId.ToString("D")))!;
        // Assert
        Assert.True(await userManager.VerifyUserTokenAsync(
            user,
            userManager.Options.Tokens.EmailConfirmationTokenProvider,
            UserManager<MonKadoUser>.ConfirmEmailTokenPurpose,
            token));
    }

    public void Dispose()
    {

        if (Directory.Exists(_keysPath))
            Directory.Delete(
                _keysPath,
                recursive: true);

        GC.SuppressFinalize(this);
    }

    private async Task<ServiceProvider> CreateProviderAsync(
        FakeEmailSender sender,
        DateTimeOffset now)
    {
        var provider = BuildProvider(
            sender,
            now);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

        return provider;
    }

    private ServiceProvider BuildProvider(
        FakeEmailSender sender,
        DateTimeOffset now)
    {
        Directory.CreateDirectory(_keysPath);
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:PostgreSql"] = fixture.Container.GetConnectionString();
        configuration["DataProtection:KeysPath"] = _keysPath;
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        services.AddSingleton<IAuthenticationEmailSender>(sender);
        services.ConfigureDataProtection(
            configuration,
            new TestHostEnvironment());
        services.ConfigureInfrastructureInjection(configuration);
        services.ConfigureAuthenticationEmailDelivery();

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string DecodeBase64Url(string token)
    {
        var base64 = token.Replace(
            '-',
            '+').Replace(
                '_',
                '/');
        base64 = base64.PadRight(
            base64.Length + ((4 - (base64.Length % 4)) % 4),
            '=');

        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static async Task<Guid> CreateUnconfirmedAccountAsync(
        ServiceProvider provider,
        DateTimeOffset now)
    {
        await using var scope = provider.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        var user = new MonKadoUser()
        {
            Id = Guid.CreateVersion7(now),
            Email = "member@example.fr",
            UserName = "member@example.fr",
            DisplayName = "Member",
            UnconfirmedAccountExpiresAt = now.UtcDateTime.AddDays(30)
        };
        var result = await userManager.CreateAsync(user);
        Assert.True(result.Succeeded);

        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var message =
            AuthenticationEmailOutboxMessage.CreateEmailConfirmation(
                user.Id,
                now.UtcDateTime);
        context.AuthenticationEmailOutboxMessages.Add(message);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        return message.Id;
    }

    private static async Task DispatchAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var dispatcher =
            scope.ServiceProvider.GetRequiredService<IAuthenticationEmailDispatcher>();
        await dispatcher.DispatchPendingAsync(
            new Uri("https://mon-kado.fr"),
            20,
            TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken);
    }

    private static async Task<int> DispatchOneAsync(ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var dispatcher =
            scope.ServiceProvider.GetRequiredService<IAuthenticationEmailDispatcher>();

        return await dispatcher.DispatchPendingAsync(
            new Uri("https://mon-kado.fr"),
            1,
            TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken);
    }

    private static async Task CreateClaimMutationTriggerAsync(
        ServiceProvider provider,
        string scenario)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var command = scenario switch
        {
            "missing-message" =>
                """
                CREATE OR REPLACE FUNCTION public.mutate_claimed_message()
                RETURNS trigger AS $$
                BEGIN
                    DELETE FROM public.authentication_email_outbox WHERE id = NEW.id;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER mutate_claimed_message
                AFTER UPDATE OF locked_until ON public.authentication_email_outbox
                FOR EACH ROW WHEN (NEW.locked_until IS NOT NULL)
                EXECUTE FUNCTION public.mutate_claimed_message();
                """,
            "processed" =>
                """
                CREATE OR REPLACE FUNCTION public.mutate_claimed_message()
                RETURNS trigger AS $$
                BEGIN
                    NEW.processed_at = NEW.locked_until;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER mutate_claimed_message
                BEFORE UPDATE OF locked_until ON public.authentication_email_outbox
                FOR EACH ROW WHEN (NEW.locked_until IS NOT NULL)
                EXECUTE FUNCTION public.mutate_claimed_message();
                """,
            "missing-lease" =>
                """
                CREATE OR REPLACE FUNCTION public.mutate_claimed_message()
                RETURNS trigger AS $$
                BEGIN
                    NEW.locked_until = NULL;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER mutate_claimed_message
                BEFORE UPDATE OF locked_until ON public.authentication_email_outbox
                FOR EACH ROW WHEN (NEW.locked_until IS NOT NULL)
                EXECUTE FUNCTION public.mutate_claimed_message();
                """,
            "expired-lease" =>
                """
                CREATE OR REPLACE FUNCTION public.mutate_claimed_message()
                RETURNS trigger AS $$
                BEGIN
                    NEW.locked_until = NEW.available_at;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER mutate_claimed_message
                BEFORE UPDATE OF locked_until ON public.authentication_email_outbox
                FOR EACH ROW WHEN (NEW.locked_until IS NOT NULL)
                EXECUTE FUNCTION public.mutate_claimed_message();
                """,
            _ => throw new InvalidOperationException($"Unknown test scenario '{scenario}'.")
        };
        await context.Database.ExecuteSqlRawAsync(
            command,
            TestContext.Current.CancellationToken);
    }

}
