using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Domain.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.IntegrationTests;

[Collection(PostgreSqlWorkerTestSuite.Name)]
public class ProfileImageCleanupTests(PostgreSqlWorkerFixture fixture) : IAsyncLifetime
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(),
        "mon-kado-profile-cleanup-tests",
        Guid
            .CreateVersion7()
            .ToString("N"));
    public ValueTask InitializeAsync()
    {

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {

        if (Directory.Exists(_testDirectory))
            Directory.Delete(
                _testDirectory,
                recursive: true);
        GC.SuppressFinalize(this);

        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ExecuteAsync_WhenPendingProfileIsReferenced_PreservesItAndCleansAbandonedImages()
    {
        // Arrange
        var now = new DateTimeOffset(
            2030,
            1,
            1,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(now);
        var store = provider.GetRequiredService<IGiftImageStore>();
        var referencedId = Guid.CreateVersion7();
        var abandonedId = Guid.CreateVersion7();
        var obsoleteId = Guid.CreateVersion7();
        var imageIds = new[]
        {
            referencedId,
            abandonedId,
            obsoleteId
        };
        foreach (var imageId in imageIds)
            await store.WritePendingAsync(
                imageId,
                new byte[]
                {
                    1,
                    2,
                    3
                },
                TestContext.Current.CancellationToken);
        foreach (var path in Directory.EnumerateFiles(
            _testDirectory,
            "*.pending",
            SearchOption.AllDirectories))
            File.SetLastWriteTimeUtc(
                path,
                now.UtcDateTime.AddHours(-2));
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        var member = CreateMember(referencedId);
        context.Users.Add(member);
        context.GiftImageDeletionOutboxMessages.Add(GiftImageDeletionOutboxMessage.Create(
                obsoleteId,
                now.UtcDateTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        // Act
        await RunCycleAsync(
            provider,
            now);
        await RunCycleAsync(
            provider,
            now);

        // Assert
        await using var retained = await store.OpenReadAsync(
            referencedId,
            TestContext.Current.CancellationToken);
        Assert.NotNull(retained);
        Assert.Null(await store.OpenReadAsync(
                abandonedId,
                TestContext.Current.CancellationToken));
        Assert.Null(await store.OpenReadAsync(
                obsoleteId,
                TestContext.Current.CancellationToken));
        Assert.Empty(await store.GetPendingAsync(
                now.UtcDateTime,
                100,
                TestContext.Current.CancellationToken));
        Assert.Empty(await context.GiftImageDeletionOutboxMessages.ToArrayAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExecuteAsync_WhenVolumeRecovers_RetriesTheDurableProfilePhotoDeletion()
    {
        // Arrange
        var now = new DateTimeOffset(
            2030,
            1,
            1,
            12,
            0,
            0,
            TimeSpan.Zero);
        await using var provider = await CreateProviderAsync(now);
        var store = provider.GetRequiredService<IGiftImageStore>();
        var imageId = Guid.CreateVersion7();
        await store.WritePendingAsync(
            imageId,
            new byte[] { 1 },
            TestContext.Current.CancellationToken);
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<MonKadoDbContext>();
        context.GiftImageDeletionOutboxMessages.Add(GiftImageDeletionOutboxMessage.Create(
                imageId,
                now.UtcDateTime));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var storagePath = Path.Combine(
            _testDirectory,
            "images");
        var offlinePath = Path.Combine(
            _testDirectory,
            "offline");
        Directory.Move(
            storagePath,
            offlinePath);
        await File.WriteAllBytesAsync(
            storagePath,
            [],
            TestContext.Current.CancellationToken);

        // Act
        await RunCycleAsync(
            provider,
            now);
        context.ChangeTracker.Clear();
        var retry = await context.GiftImageDeletionOutboxMessages.SingleAsync(TestContext.Current.CancellationToken);
        File.Delete(storagePath);
        Directory.Move(
            offlinePath,
            storagePath);
        await RunCycleAsync(
            provider,
            now.AddMinutes(1));

        // Assert
        Assert.Equal(
            1,
            retry.AttemptCount);
        Assert.Empty(await context.GiftImageDeletionOutboxMessages.ToArrayAsync(TestContext.Current.CancellationToken));
        Assert.Null(await store.OpenReadAsync(
                imageId,
                TestContext.Current.CancellationToken));
    }

    private async Task<ServiceProvider> CreateProviderAsync(DateTimeOffset now)
    {
        await fixture.ResetDatabaseAsync(TestContext.Current.CancellationToken);
        var configuration = new ConfigurationManager();
        configuration["ConnectionStrings:PostgreSql"] = fixture.Container.GetConnectionString();
        configuration["GiftImages:StoragePath"] = Path.Combine(
            _testDirectory,
            "images");
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        services.ConfigureInfrastructureInjection(configuration);
        services.ConfigureImageInfrastructureInjection(configuration);

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static MonKadoUser CreateMember(Guid imageId)
    {
        var member = new MonKadoUser
        {
            Id = Guid.CreateVersion7(),
            DisplayName = "Photo owner",
            Email = "photo-owner@example.test",
            NormalizedEmail = "PHOTO-OWNER@EXAMPLE.TEST",
            UserName = "photo-owner@example.test",
            NormalizedUserName = "PHOTO-OWNER@EXAMPLE.TEST",
            EmailConfirmed = true
        };
        member.SetProfileImage(
            imageId,
            new byte[32]);

        return member;
    }

    private static async Task RunCycleAsync(
        ServiceProvider provider,
        DateTimeOffset now)
    {
        var timeProvider = new CleanupCycleTimeProvider(now);
        using var worker = new GiftImageCleanupWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            provider.GetRequiredService<IGiftImageStore>(),
            timeProvider,
            Microsoft.Extensions.Options.Options.Create(new GiftImageCleanupOptions()),
            NullLogger<GiftImageCleanupWorker>.Instance);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await timeProvider.CycleCompleted.WaitAsync(TestContext.Current.CancellationToken);
        await worker.StopAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(worker.ExecuteTask);
        await worker.ExecuteTask.WaitAsync(TestContext.Current.CancellationToken);
    }
}
