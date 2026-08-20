using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public sealed class ExpiredAuthenticationSessionCleanupWorkerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 20, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CleanupContinuesThroughFullBatchesAndReturnsTotal()
    {
        RecordingCleanup cleanup = new(500, 500, 2);
        ServiceCollection services = new();
        services.AddSingleton<IExpiredAuthenticationSessionCleanup>(cleanup);
        await using ServiceProvider provider = services.BuildServiceProvider();
        ExpiredAuthenticationSessionCleanupWorker worker = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(Now),
            NullLogger<ExpiredAuthenticationSessionCleanupWorker>.Instance);

        int deletedCount = await worker.DeleteExpiredSessions(
            TestContext.Current.CancellationToken);

        Assert.Equal(1_002, deletedCount);
        Assert.Equal(3, cleanup.Calls.Count);
        Assert.All(cleanup.Calls, call =>
        {
            Assert.Equal(Now, call.Cutoff);
            Assert.Equal(500, call.BatchSize);
        });
    }

    [Fact]
    public async Task CleanupStopsAfterAnEmptyFirstBatch()
    {
        RecordingCleanup cleanup = new(0);
        ServiceCollection services = new();
        services.AddSingleton<IExpiredAuthenticationSessionCleanup>(cleanup);
        await using ServiceProvider provider = services.BuildServiceProvider();
        ExpiredAuthenticationSessionCleanupWorker worker = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FixedTimeProvider(Now),
            NullLogger<ExpiredAuthenticationSessionCleanupWorker>.Instance);

        int deletedCount = await worker.DeleteExpiredSessions(
            TestContext.Current.CancellationToken);

        Assert.Equal(0, deletedCount);
        Assert.Single(cleanup.Calls);
    }

    private sealed class RecordingCleanup(params int[] results) : IExpiredAuthenticationSessionCleanup
    {
        private readonly Queue<int> remainingResults = new(results);

        public List<CleanupCall> Calls { get; } = [];

        public Task<int> DeleteExpiredSessionsAsync(
            DateTimeOffset cutoff,
            int batchSize,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Add(new CleanupCall(cutoff, batchSize));
            return Task.FromResult(remainingResults.Dequeue());
        }
    }

    private sealed record CleanupCall(DateTimeOffset Cutoff, int BatchSize);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
