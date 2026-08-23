using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class ExpiredMemberEmailChangeRequestCleanupWorkerTests
{
    private static readonly DateTimeOffset _now = new(
        2026,
        8,
        22,
        20,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_WhenHostStopsDuringDelay_CompletesCleanly()
    {
        // Arrange
        var cleanup = new RecordingEmailChangeRequestCleanup(0);
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(provider);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await cleanup.Called.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Act
        await worker.StopAsync(TestContext.Current.CancellationToken);
        var executeTask = GetExecuteTask(worker);

        // Assert
        Assert.True(executeTask.IsCompletedSuccessfully);
        Assert.Single(cleanup.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFullBatchesAreDeleted_ContinuesUntilPartialBatch()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cleanup = new RecordingEmailChangeRequestCleanup(
            500,
            500,
            2)
        {
            OnCall = callCount =>
            {

                if (callCount == 3)
                    source.Cancel();
            }
        };
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(
            provider,
            new ImmediateTimeProvider(_now));

        // Act
        await worker.StartAsync(source.Token);
        var executeTask = GetExecuteTask(worker);
        await executeTask.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
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
    public async Task ExecuteAsync_WhenDelayCompletes_StartsNextCleanupCycle()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cleanup = new RecordingEmailChangeRequestCleanup(
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
            new ImmediateTimeProvider(_now));

        // Act
        await worker.StartAsync(source.Token);
        var executeTask = GetExecuteTask(worker);
        await executeTask.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            2,
            cleanup.Calls.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenCleanupFails_StopsCleanlyAfterCancellation(
        bool isCancellation)
    {
        // Arrange
        using var source = new CancellationTokenSource();
        Exception exception = isCancellation
            ? new OperationCanceledException()
            : new InvalidOperationException();
        var cleanup = new ThrowingEmailChangeRequestCleanup(exception)
        {
            OnCall = source.Cancel
        };
        await using var provider = CreateProvider(cleanup);
        var logger = new RecordingLogger<ExpiredMemberEmailChangeRequestCleanupWorker>();
        var worker = CreateWorker(
            provider,
            logger: logger);

        // Act
        await worker.StartAsync(source.Token);
        var executeTask = GetExecuteTask(worker);
        await executeTask.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(executeTask.IsCompletedSuccessfully);

        if (isCancellation)
        {
            Assert.Empty(logger.Entries);

            return;
        }

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(
            LogLevel.Error,
            entry.LogLevel);
        Assert.Same(
            exception,
            entry.Exception);
    }

    private static ServiceProvider CreateProvider(IExpiredMemberEmailChangeRequestCleanup cleanup)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cleanup);

        return services.BuildServiceProvider();
    }

    private static ExpiredMemberEmailChangeRequestCleanupWorker CreateWorker(
        ServiceProvider provider,
        TimeProvider? timeProvider = null,
        ILogger<ExpiredMemberEmailChangeRequestCleanupWorker>? logger = null)
    {

        return new ExpiredMemberEmailChangeRequestCleanupWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            timeProvider ?? new FixedTimeProvider(_now),
            logger ?? NullLogger<ExpiredMemberEmailChangeRequestCleanupWorker>.Instance);
    }

    private static Task GetExecuteTask(BackgroundService worker)
    {
        var executeTask = worker.ExecuteTask;
        Assert.NotNull(executeTask);

        return executeTask;
    }
}
