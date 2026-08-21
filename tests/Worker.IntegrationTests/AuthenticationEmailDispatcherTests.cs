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

    [Fact]
    public async Task ExecuteAsync_WhenConfirmedAccount_IsNotContactedAndMessageIsClosed()
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
            await setupContext.Users.ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    user => user.EmailConfirmed,
                    true),
                TestContext.Current.CancellationToken);
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

}
