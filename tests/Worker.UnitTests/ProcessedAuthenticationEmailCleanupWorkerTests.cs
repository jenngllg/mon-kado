using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class ProcessedAuthenticationEmailCleanupWorkerTests
{
    private const int RetentionDays = 30;
    private static readonly DateTimeOffset _now = new(
        2026,
        8,
        24,
        10,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_WhenHostStopsDuringDelay_CompletesCleanly()
    {
        // Arrange
        var cleanup = new RecordingProcessedAuthenticationEmailCleanup(0);
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
    public async Task ExecuteAsync_WhenCleanupCompletes_SchedulesNextRunAfterTwentyFourHours()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cleanup = new RecordingProcessedAuthenticationEmailCleanup(0);
        await using var provider = CreateProvider(cleanup);
        var timeProvider = new CapturingTimeProvider(
            _now,
            source);
        var worker = CreateWorker(
            provider,
            timeProvider);

        // Act
        await worker.StartAsync(source.Token);
        var executeTask = GetExecuteTask(worker);
        await executeTask.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            TimeSpan.FromHours(24),
            timeProvider.DueTime);
        Assert.Single(cleanup.Calls);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDelayCompletes_StartsNextIteration()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cleanup = new RecordingProcessedAuthenticationEmailCleanup(
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
        Assert.True(executeTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFullBatchesAreDeleted_UsesStableCutoffUntilPartialBatch()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cleanup = new RecordingProcessedAuthenticationEmailCleanup(
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
        var logger = new RecordingLogger<ProcessedAuthenticationEmailCleanupWorker>();
        var worker = CreateWorker(
            provider,
            new ImmediateTimeProvider(_now),
            logger);

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
                    _now.UtcDateTime.AddDays(-RetentionDays),
                    call.Cutoff);
                Assert.Equal(
                    500,
                    call.BatchSize);
            });
        Assert.All(
            cleanup.CancellationTokens,
            cancellationToken => Assert.Equal(
                cleanup.CancellationTokens[0],
                cancellationToken));
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(
            LogLevel.Information,
            entry.LogLevel);
        Assert.Equal(
            LogEventIds.ProcessedAuthenticationEmailsDeleted,
            entry.EventId.Id);
        Assert.Contains(
            "1002",
            entry.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenCleanupFails_StopsCleanlyAfterCancellation(
        bool isCancellation)
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var exception = isCancellation
            ? (Exception)new OperationCanceledException()
            : new InvalidOperationException();
        var timeProvider = new CapturingTimeProvider(
            _now,
            source);
        var cleanup = new ThrowingProcessedAuthenticationEmailCleanup(exception)
        {
            OnCall = isCancellation
                ? source.Cancel
                : null
        };
        await using var provider = CreateProvider(cleanup);
        var logger = new RecordingLogger<ProcessedAuthenticationEmailCleanupWorker>();
        var worker = CreateWorker(
            provider,
            timeProvider,
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
        Assert.Equal(
            LogEventIds.ProcessedAuthenticationEmailCleanupFailed,
            entry.EventId.Id);
        Assert.Same(
            exception,
            entry.Exception);
        Assert.Equal(
            TimeSpan.FromMinutes(15),
            timeProvider.DueTime);
    }

    private static ServiceProvider CreateProvider(IProcessedAuthenticationEmailCleanup cleanup)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cleanup);

        return services.BuildServiceProvider();
    }

    private static ProcessedAuthenticationEmailCleanupWorker CreateWorker(
        ServiceProvider provider,
        TimeProvider? timeProvider = null,
        ILogger<ProcessedAuthenticationEmailCleanupWorker>? logger = null)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new AuthenticationEmailOptions
        {
            ProcessedRetentionDays = RetentionDays
        });

        return new ProcessedAuthenticationEmailCleanupWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            timeProvider ?? new FixedTimeProvider(_now),
            options,
            logger ?? NullLogger<ProcessedAuthenticationEmailCleanupWorker>.Instance);
    }

    private static Task GetExecuteTask(BackgroundService worker)
    {
        var executeTask = worker.ExecuteTask;
        Assert.NotNull(executeTask);

        return executeTask;
    }
}
