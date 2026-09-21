using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;
using JennGllg.Fr.MonKado.Back.Tests.Common;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class ExpiredGuestSessionCleanupWorkerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenCycleFinishes_ReportsActualNextSchedule(bool fails)
    {
        // Arrange
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var clock = new CapturingTimeProvider(
            DateTimeOffset.UnixEpoch,
            stopping);
        using var telemetry = new ApplicationTelemetry(
            clock,
            Microsoft.Extensions.Options.Options.Create(new ObservabilityOptions { Service = "worker" }));
        IExpiredGuestSessionCleanup cleanup = fails
            ? new ThrowingGuestSessionCleanup(new InvalidOperationException())
            : new RecordingGuestSessionCleanup(0);
        await using var provider = CreateProvider(cleanup);
        using var worker = new ExpiredGuestSessionCleanupWorker(
            telemetry,
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            Microsoft.Extensions.Options.Options.Create(new AuthenticationCleanupOptions()),
            NullLogger<ExpiredGuestSessionCleanupWorker>.Instance);

        // Act
        await worker.StartAsync(stopping.Token);
        await GetExecuteTask(worker).WaitAsync(TestContext.Current.CancellationToken);
        var snapshot = telemetry.Capture().Jobs["ExpiredGuestSessionCleanup"];

        // Assert
        Assert.Equal("waiting", snapshot.State);
        Assert.Equal(DateTime.UnixEpoch, snapshot.StartedAt);
        Assert.Equal(DateTime.UnixEpoch, snapshot.LastCompletedAt);
        Assert.Equal(DateTime.UnixEpoch + clock.DueTime.GetValueOrDefault(), snapshot.NextExpectedAt);
        Assert.Equal(fails ? 1 : 0, snapshot.ConsecutiveFailures);
        Assert.Equal(fails ? 0 : 1, snapshot.Successes);
        Assert.Equal(fails ? TimeSpan.FromMinutes(15) : TimeSpan.FromHours(24), clock.DueTime);
    }

    private static readonly DateTimeOffset _now = new(
        2026,
        8,
        26,
        12,
        0,
        0,
        TimeSpan.Zero);

    [Fact]
    public async Task ExecuteAsync_WhenHostStopsDuringDelay_CompletesCleanly()
    {
        // Arrange
        var cleanup = new RecordingGuestSessionCleanup(0);
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(provider);
        await worker.StartAsync(TestContext.Current.CancellationToken);
        await cleanup.Called.Task.WaitAsync(TestContext.Current.CancellationToken);

        // Act
        await worker.StopAsync(TestContext.Current.CancellationToken);
        var executeTask = GetExecuteTask(worker);

        // Assert
        Assert.True(executeTask.IsCompletedSuccessfully);
        Assert.Equal(
            [500],
            cleanup.BatchSizes);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDelayCompletes_StartsNextIteration()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cleanup = new RecordingGuestSessionCleanup(
            0,
            0)
        {
            OnCall = count =>
            {
                if (count == 2)
                    source.Cancel();
            }
        };
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(
            provider,
            new ImmediateTimeProvider(_now));

        // Act
        await worker.StartAsync(source.Token);
        await GetExecuteTask(worker).WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(GetExecuteTask(worker).IsCompletedSuccessfully);
        Assert.Equal(
            [500, 500],
            cleanup.BatchSizes);
    }

    [Fact]
    public async Task ExecuteAsync_WhenFullBatchesAreDeleted_ContinuesUntilPartialBatch()
    {
        // Arrange
        using var source = new CancellationTokenSource();
        var cleanup = new RecordingGuestSessionCleanup(
            500,
            500,
            2)
        {
            OnCall = count =>
            {
                if (count == 3)
                    source.Cancel();
            }
        };
        await using var provider = CreateProvider(cleanup);
        var logger = new RecordingLogger<ExpiredGuestSessionCleanupWorker>();
        var worker = CreateWorker(
            provider,
            new ImmediateTimeProvider(_now),
            logger);

        // Act
        await worker.StartAsync(source.Token);
        await GetExecuteTask(worker).WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [500, 500, 500],
            cleanup.BatchSizes);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(
            LogLevel.Information,
            entry.LogLevel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecuteAsync_WhenCleanupFails_StopsCleanlyAfterCancellation(bool isCancellation)
    {
        // Arrange
        using var source = new CancellationTokenSource();
        Exception exception = isCancellation
            ? new OperationCanceledException()
            : new InvalidOperationException();
        var cleanup = new ThrowingGuestSessionCleanup(exception)
        {
            OnCall = source.Cancel
        };
        await using var provider = CreateProvider(cleanup);
        var logger = new RecordingLogger<ExpiredGuestSessionCleanupWorker>();
        var worker = CreateWorker(
            provider,
            logger: logger);

        // Act
        await worker.StartAsync(source.Token);
        await GetExecuteTask(worker).WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(GetExecuteTask(worker).IsCompletedSuccessfully);

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

    private static ServiceProvider CreateProvider(IExpiredGuestSessionCleanup cleanup)
    {
        var services = new ServiceCollection();
        services.AddTestTelemetry();
        services.AddSingleton(cleanup);

        return services.BuildServiceProvider();
    }

    private static ExpiredGuestSessionCleanupWorker CreateWorker(
        ServiceProvider provider,
        TimeProvider? timeProvider = null,
        ILogger<ExpiredGuestSessionCleanupWorker>? logger = null)
    {
        return new ExpiredGuestSessionCleanupWorker(
            provider.GetRequiredService<IApplicationTelemetry>(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            timeProvider ?? new FixedTimeProvider(_now),
            Microsoft.Extensions.Options.Options.Create(new AuthenticationCleanupOptions()),
            logger ?? NullLogger<ExpiredGuestSessionCleanupWorker>.Instance);
    }

    private static Task GetExecuteTask(BackgroundService worker)
    {
        var executeTask = worker.ExecuteTask;
        Assert.NotNull(executeTask);

        return executeTask;
    }
}
