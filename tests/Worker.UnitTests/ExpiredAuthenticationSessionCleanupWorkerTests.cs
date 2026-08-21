using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Handlers;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class ExpiredAuthenticationSessionCleanupWorkerTests
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
        var cleanup = new RecordingCleanup(0);
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(provider);
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
        var cleanup = new RecordingCleanup(
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
            new ImmediateTimeProvider());

        // Act
        await worker.StartAsync(source.Token);
        await worker.ExecuteTask!.WaitAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCleanupContinuesThroughFullBatchesAnd_ReturnsTotal()
    {
        // Arrange
        var cleanup = new RecordingCleanup(
            500,
            500,
            2);
        var services = new ServiceCollection();
        services.AddSingleton<IExpiredAuthenticationSessionCleanup>(cleanup);
        await using var provider = services.BuildServiceProvider();
        var worker = new ExpiredAuthenticationSessionCleanupWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(_now),
            NullLogger<ExpiredAuthenticationSessionCleanupWorker>.Instance);

        // Act
        var deletedCount = await worker.DeleteExpiredSessionsAsync(
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
    public async Task ExecuteAsync_WhenCleanup_StopsAfterAnEmptyFirstBatch()
    {
        // Arrange
        var cleanup = new RecordingCleanup(0);
        var services = new ServiceCollection();
        services.AddSingleton<IExpiredAuthenticationSessionCleanup>(cleanup);
        await using var provider = services.BuildServiceProvider();
        var worker = new ExpiredAuthenticationSessionCleanupWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(_now),
            NullLogger<ExpiredAuthenticationSessionCleanupWorker>.Instance);

        // Act
        var deletedCount = await worker.DeleteExpiredSessionsAsync(
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            0,
            deletedCount);
        Assert.Single(cleanup.Calls);
    }

    [Fact]
    public async Task CleanupOnceAsync_WhenSessionsAreDeleted_ReturnsNormalInterval()
    {
        // Arrange
        var cleanup = new RecordingCleanup(2);
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(provider);

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
        var cleanup = new ThrowingSessionCleanup(new InvalidOperationException());
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(provider);

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
        var cleanup = new ThrowingSessionCleanup(new OperationCanceledException(source.Token));
        await using var provider = CreateProvider(cleanup);
        var worker = CreateWorker(provider);

        // Act
        Task<TimeSpan> action() => worker.CleanupOnceAsync(source.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>((Func<Task<TimeSpan>>)action);
    }

    private static ServiceProvider CreateProvider(IExpiredAuthenticationSessionCleanup cleanup)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cleanup);

        return services.BuildServiceProvider();
    }

    private static ExpiredAuthenticationSessionCleanupWorker CreateWorker(
        ServiceProvider provider,
        TimeProvider? timeProvider = null)
    {
        return new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            timeProvider ?? new FixedTimeProvider(_now),
            NullLogger<ExpiredAuthenticationSessionCleanupWorker>.Instance);
    }

}
