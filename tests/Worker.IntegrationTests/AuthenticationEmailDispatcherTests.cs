using System.Collections.Concurrent;
using System.Text;
using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace JennGllg.Fr.MonKado.Back.Worker.IntegrationTests;

[Collection(PostgreSqlWorkerTestSuite.Name)]
public sealed class AuthenticationEmailDispatcherTests(PostgreSqlWorkerFixture fixture) : IDisposable
{
    private readonly string keysPath = Path.Combine(
        Path.GetTempPath(),
        "mon-kado-worker-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AcceptedMessageIsProcessedAndProviderIdentifierIsStored()
    {
        FakeEmailSender sender = new();
        DateTimeOffset now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        await using ServiceProvider provider = await CreateProvider(sender, now);
        Guid messageId = await CreateUnconfirmedAccount(provider, now);

        await Dispatch(provider);

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        AuthenticationEmailOutboxMessage message = await context.AuthenticationEmailOutboxMessages
            .SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(messageId, sender.Messages.Single().OutboxMessageId);
        Assert.Equal("fake-provider-id", message.ProviderMessageId);
        Assert.Equal(now, message.ProcessedAt);
        Assert.Null(message.LockedUntil);
        Assert.Null(message.LastError);
    }

    [Fact]
    public async Task TransientFailureReleasesLeaseAndSchedulesFirstRetry()
    {
        FakeEmailSender sender = new(fail: true);
        DateTimeOffset now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        await using ServiceProvider provider = await CreateProvider(sender, now);
        await CreateUnconfirmedAccount(provider, now);

        await Dispatch(provider);

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        AuthenticationEmailOutboxMessage message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, message.AttemptCount);
        Assert.Equal(now.AddMinutes(1), message.AvailableAt);
        Assert.Equal("TRANSIENT", message.LastError);
        Assert.Null(message.LockedUntil);
        Assert.Null(message.ProcessedAt);
    }

    [Fact]
    public async Task ProviderRetryAfterOverridesShorterConfiguredDelay()
    {
        FakeEmailSender sender = new(fail: true, retryAfter: TimeSpan.FromHours(2));
        DateTimeOffset now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        await using ServiceProvider provider = await CreateProvider(sender, now);
        await CreateUnconfirmedAccount(provider, now);

        await Dispatch(provider);

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        AuthenticationEmailOutboxMessage message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(now.AddHours(2), message.AvailableAt);
    }

    [Fact]
    public async Task AuthenticationFailureUsesSlowRetryImmediately()
    {
        FakeEmailSender sender = new(
            fail: true,
            failureCategory: AuthenticationEmailFailureCategory.Authentication);
        DateTimeOffset now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        await using ServiceProvider provider = await CreateProvider(sender, now);
        await CreateUnconfirmedAccount(provider, now);

        await Dispatch(provider);

        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        AuthenticationEmailOutboxMessage message = await scope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(now.AddHours(6), message.AvailableAt);
        Assert.Equal("AUTHENTICATION", message.LastError);
    }

    [Fact]
    public async Task ExpiredLeaseCanBeClaimedAgain()
    {
        FakeEmailSender sender = new();
        DateTimeOffset now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        await using ServiceProvider provider = await CreateProvider(sender, now);
        await CreateUnconfirmedAccount(provider, now);
        await using (AsyncServiceScope setupScope = provider.CreateAsyncScope())
        {
            MonKadoDbContext setupContext =
                setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await setupContext.AuthenticationEmailOutboxMessages.ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(message => message.LockedUntil, now.AddMinutes(-1))
                    .SetProperty(message => message.AttemptCount, 1),
                TestContext.Current.CancellationToken);
        }

        await Dispatch(provider);

        Assert.Single(sender.Messages);
        await using AsyncServiceScope assertionScope = provider.CreateAsyncScope();
        AuthenticationEmailOutboxMessage message = await assertionScope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, message.AttemptCount);
        Assert.NotNull(message.ProcessedAt);
    }

    [Fact]
    public async Task ConcurrentDispatchersClaimMessageOnlyOnce()
    {
        FakeEmailSender sender = new(delay: TimeSpan.FromMilliseconds(100));
        DateTimeOffset now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        await using ServiceProvider provider = await CreateProvider(sender, now);
        await CreateUnconfirmedAccount(provider, now);

        await Task.WhenAll(Dispatch(provider), Dispatch(provider));

        Assert.Single(sender.Messages);
    }

    [Fact]
    public async Task ConfirmedAccountIsNotContactedAndMessageIsClosed()
    {
        FakeEmailSender sender = new();
        DateTimeOffset now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        await using ServiceProvider provider = await CreateProvider(sender, now);
        await CreateUnconfirmedAccount(provider, now);
        await using (AsyncServiceScope setupScope = provider.CreateAsyncScope())
        {
            MonKadoDbContext setupContext =
                setupScope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
            await setupContext.Users.ExecuteUpdateAsync(
                setters => setters.SetProperty(user => user.EmailConfirmed, true),
                TestContext.Current.CancellationToken);
        }

        await Dispatch(provider);

        Assert.Empty(sender.Messages);
        await using AsyncServiceScope assertionScope = provider.CreateAsyncScope();
        AuthenticationEmailOutboxMessage message = await assertionScope.ServiceProvider
            .GetRequiredService<MonKadoDbContext>()
            .AuthenticationEmailOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(now, message.ProcessedAt);
    }

    [Fact]
    public async Task SeparateInstanceSharingKeysCanValidateGeneratedToken()
    {
        FakeEmailSender sender = new();
        DateTimeOffset now = new(2026, 8, 13, 12, 0, 0, TimeSpan.Zero);
        await using ServiceProvider sendingProvider = await CreateProvider(sender, now);
        await CreateUnconfirmedAccount(sendingProvider, now);
        await Dispatch(sendingProvider);

        await using ServiceProvider validatingProvider = BuildProvider(new FakeEmailSender(), now);
        AuthenticationEmailMessage delivery = sender.Messages.Single();
        Dictionary<string, string> fragment = delivery.ConfirmationUrl.Fragment
            .TrimStart('#')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
        Guid userId = Guid.Parse(fragment["userId"]);
        string token = DecodeBase64Url(fragment["token"]);

        await using AsyncServiceScope scope = validatingProvider.CreateAsyncScope();
        UserManager<MonKadoUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        MonKadoUser user = (await userManager.FindByIdAsync(userId.ToString("D")))!;
        Assert.True(await userManager.VerifyUserTokenAsync(
            user,
            userManager.Options.Tokens.EmailConfirmationTokenProvider,
            UserManager<MonKadoUser>.ConfirmEmailTokenPurpose,
            token));
    }

    public void Dispose()
    {
        if (Directory.Exists(keysPath))
        {
            Directory.Delete(keysPath, recursive: true);
        }
    }

    private async Task<ServiceProvider> CreateProvider(FakeEmailSender sender, DateTimeOffset now)
    {
        ServiceProvider provider = BuildProvider(sender, now);
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        return provider;
    }

    private ServiceProvider BuildProvider(FakeEmailSender sender, DateTimeOffset now)
    {
        Directory.CreateDirectory(keysPath);
        ConfigurationManager configuration = new();
        configuration["ConnectionStrings:PostgreSql"] = fixture.Container.GetConnectionString();
        configuration["DataProtection:KeysPath"] = keysPath;
        ServiceCollection services = new();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        services.AddSingleton<IAuthenticationEmailSender>(sender);
        services.AddMonKadoDataProtection(configuration, new TestHostEnvironment());
        services.AddPostgreSqlPersistence(configuration);
        services.AddAuthenticationEmailDelivery();
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static string DecodeBase64Url(string token)
    {
        string base64 = token.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    private static async Task<Guid> CreateUnconfirmedAccount(ServiceProvider provider, DateTimeOffset now)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        UserManager<MonKadoUser> userManager = scope.ServiceProvider.GetRequiredService<UserManager<MonKadoUser>>();
        MonKadoUser user = new()
        {
            Id = Guid.CreateVersion7(now),
            Email = "member@example.fr",
            UserName = "member@example.fr",
            DisplayName = "Member",
            CreatedAt = now,
            UpdatedAt = now,
            UnconfirmedAccountExpiresAt = now.AddDays(30)
        };
        IdentityResult result = await userManager.CreateAsync(user);
        Assert.True(result.Succeeded);

        MonKadoDbContext context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        AuthenticationEmailOutboxMessage message =
            AuthenticationEmailOutboxMessage.CreateEmailConfirmation(user.Id, now);
        context.AuthenticationEmailOutboxMessages.Add(message);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return message.Id;
    }

    private static async Task Dispatch(ServiceProvider provider)
    {
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IAuthenticationEmailDispatcher dispatcher =
            scope.ServiceProvider.GetRequiredService<IAuthenticationEmailDispatcher>();
        await dispatcher.DispatchPendingAsync(
            new Uri("https://mon-kado.fr"),
            20,
            TimeSpan.FromMinutes(2),
            TestContext.Current.CancellationToken);
    }

    private sealed class FakeEmailSender(
        bool fail = false,
        TimeSpan? delay = null,
        TimeSpan? retryAfter = null,
        AuthenticationEmailFailureCategory failureCategory = AuthenticationEmailFailureCategory.Transient)
        : IAuthenticationEmailSender
    {
        public ConcurrentQueue<AuthenticationEmailMessage> Messages { get; } = new();

        public async Task<AuthenticationEmailSendResult> SendEmailConfirmationAsync(
            AuthenticationEmailMessage message,
            CancellationToken cancellationToken)
        {
            Messages.Enqueue(message);
            if (delay is { } value)
            {
                await Task.Delay(value, cancellationToken);
            }

            if (fail)
            {
                throw new AuthenticationEmailDeliveryException(
                    failureCategory,
                    retryAfter);
            }

            return new AuthenticationEmailSendResult("fake-provider-id");
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Local";

        public string ApplicationName { get; set; } = "Worker.IntegrationTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
