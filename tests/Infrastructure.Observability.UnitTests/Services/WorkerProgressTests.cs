using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.UnitTests.Services;

public class WorkerProgressTests
{
    private readonly ControlledTimeProvider _timeProvider = new();
    private readonly WorkerProgress _progress;

    public WorkerProgressTests()
    {
        _progress = new WorkerProgress(_timeProvider);
    }

    [Fact]
    public void Capture_WhenNotStarted_ReportsExpectedStartup()
    {
        // Arrange / Act
        var snapshot = _progress.Capture();

        // Assert
        Assert.Equal("waiting", snapshot.State);
        Assert.Equal(_timeProvider.GetUtcNow().UtcDateTime, snapshot.NextExpectedAt);
        Assert.Null(snapshot.StartedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Complete_WhenCycleEnds_ReportsActualProgress(bool successful)
    {
        // Arrange
        var started = _timeProvider.GetUtcNow().UtcDateTime;
        _progress.Begin();
        var running = _progress.Capture();
        _timeProvider.Advance(TimeSpan.FromSeconds(3));

        // Act
        var duration = _progress.Complete(
            TimeSpan.FromHours(24),
            successful);
        var completed = _progress.Capture();

        // Assert
        Assert.Equal("running", running.State);
        Assert.Null(running.NextExpectedAt);
        Assert.Equal(started, running.StartedAt);
        Assert.Equal(3000, duration);
        Assert.Equal("waiting", completed.State);
        Assert.Equal(started.AddSeconds(3), completed.LastCompletedAt);
        Assert.Equal(started.AddHours(24).AddSeconds(3), completed.NextExpectedAt);
        Assert.Equal(successful ? 1 : 0, completed.Successes);
        Assert.Equal(successful ? 0 : 1, completed.Failures);
        Assert.Equal(successful ? 0 : 1, completed.ConsecutiveFailures);
        Assert.Equal(duration, completed.LastDurationMilliseconds);
    }

    [Fact]
    public void Complete_WhenRecovered_ResetsOnlyConsecutiveFailures()
    {
        // Arrange
        _progress.Begin();
        _progress.Complete(
            TimeSpan.Zero,
            false);
        _progress.RecordTerminalFailure();
        _progress.Begin();

        // Act
        _progress.Complete(
            TimeSpan.FromMinutes(1),
            true);
        _progress.Disable();
        var snapshot = _progress.Capture();

        // Assert
        Assert.Equal(0, snapshot.ConsecutiveFailures);
        Assert.Equal(1, snapshot.Failures);
        Assert.Equal(1, snapshot.TerminalFailures);
        Assert.Equal("disabled", snapshot.State);
        Assert.Null(snapshot.NextExpectedAt);
    }
}
