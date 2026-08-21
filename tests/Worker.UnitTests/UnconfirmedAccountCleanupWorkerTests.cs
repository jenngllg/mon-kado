using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class UnconfirmedAccountCleanupWorkerTests
{
    private static readonly DateTimeOffset _now = new(
        2026,
        8,
        20,
        10,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_WhenHostStopsDuringDelay_CompletesCleanly()
    {
        // Arrange
        var cleanup = new RecordingAccountCleanup(0);
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(
            provider,
            _now);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await cleanup.Called.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Act
        await worker.StopAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Single(cleanup.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDelayCompletes_StartsNextIteration()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cleanup = new RecordingAccountCleanup(
            0,
            0)
        {
            OnCall = callCount =>
            {

                if (callCount == 2)
                    source.Cancel();
            }
        };
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(
            provider,
            _now,
            new ImmediateTimeProvider());

        // Act
        await worker.StartAsync(source.Token);
        await worker.ExecuteTask!.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task DeleteExpiredAccountsAsync_WhenFullBatchesAreReturned_ReturnsTotal()
    {
        // Arrange
        var cleanup = new RecordingAccountCleanup(
            500,
            500,
            2);
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(
            provider,
            _now);

        // Act
        var deletedCount = await worker.DeleteExpiredAccountsAsync(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            1_002,
            deletedCount);
        Assert.Equal(
            3,
            cleanup.Calls.Count);
        Assert.All(
            cleanup.Calls,
            call =>
            {
                Assert.Equal(
                    _now.UtcDateTime,
                    call.Cutoff);
                Assert.Equal(
                    500,
                    call.BatchSize);
            });
    }

    [Fact]
    public async Task DeleteExpiredAccountsAsync_WhenFirstBatchIsEmpty_ReturnsZero()
    {
        // Arrange
        var cleanup = new RecordingAccountCleanup(0);
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(
            provider,
            _now);

        // Act
        var deletedCount = await worker.DeleteExpiredAccountsAsync(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            0,
            deletedCount);
        Assert.Single(cleanup.Calls);
    }

    [Fact]
    public async Task CleanupOnceAsync_WhenAccountsAreDeleted_ReturnsNormalInterval()
    {
        // Arrange
        var cleanup = new RecordingAccountCleanup(2);
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(
            provider,
            _now);

        // Act
        var delay = await worker.CleanupOnceAsync(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            TimeSpan.FromHours(24),
            delay);
    }

    [Fact]
    public async Task CleanupOnceAsync_WhenCleanupFails_ReturnsFailureInterval()
    {
        // Arrange
        var cleanup = new ThrowingAccountCleanup(new InvalidOperationException());
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(
            provider,
            _now);

        // Act
        var delay = await worker.CleanupOnceAsync(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            delay);
    }

    [Fact]
    public async Task CleanupOnceAsync_WhenCleanupIsCanceled_PreservesCancellation()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        source.Cancel();
        var cleanup = new ThrowingAccountCleanup(new OperationCanceledException(source.Token));
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(
            provider,
            _now);

        // Act
        Task<TimeSpan> action() => worker.CleanupOnceAsync(source.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>((Func<Task<TimeSpan>>)action);
    }

    private static ServiceProvider CreateProvider(IExpiredAccountCleanup cleanup)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cleanup);

        return services.BuildServiceProvider();
    }

    private static UnconfirmedAccountCleanupWorker CreateWorker(
        ServiceProvider provider,
        DateTimeOffset now,
        TimeProvider? timeProvider = null)
    {
        return new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            timeProvider ?? new FixedTimeProvider(now),
            NullLogger<UnconfirmedAccountCleanupWorker>.Instance);
    }
}
