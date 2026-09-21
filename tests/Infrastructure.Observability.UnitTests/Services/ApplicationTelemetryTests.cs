using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

using Microsoft.Extensions.Options;

using System.Diagnostics.Metrics;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.UnitTests.Services;

public class ApplicationTelemetryTests
{
    private readonly ControlledTimeProvider _timeProvider = new();

    [Theory]
    [InlineData(200, false, 0, 0, 0)]
    [InlineData(429, false, 0, 1, 0)]
    [InlineData(503, false, 1, 0, 0)]
    [InlineData(503, true, 0, 0, 1)]
    public void RecordHttp_WhenCompleted_UsesBoundedCounters(
        int status,
        bool abandoned,
        long errors,
        long throttled,
        long abandonedCount)
    {
        // Arrange
        using var telemetry = new ApplicationTelemetry(
            _timeProvider,
            Microsoft.Extensions.Options.Options.Create(new ObservabilityOptions()));

        // Act
        telemetry.RecordHttp(
            status,
            TimeSpan.FromMilliseconds(2100),
            abandoned);
        var snapshot = telemetry.Capture();

        // Assert
        Assert.Equal(1, snapshot.Http.Requests);
        Assert.Equal(errors, snapshot.Http.ServerErrors);
        Assert.Equal(throttled, snapshot.Http.Throttled);
        Assert.Equal(abandonedCount, snapshot.Http.Abandoned);
        Assert.Equal(1, snapshot.Http.DurationBuckets[6]);
        Assert.Equal(1, snapshot.Http.DurationBuckets.Sum());
        Assert.Empty(snapshot.Jobs);
        Assert.Equal("api", snapshot.Service);
        Assert.Equal(32, snapshot.BootId.Length);
    }

    [Fact]
    public void Capture_WhenWorkerRuns_DeclaresAllJobsAndCopiesConsistentState()
    {
        // Arrange
        using var telemetry = new ApplicationTelemetry(
            _timeProvider,
            Microsoft.Extensions.Options.Options.Create(new ObservabilityOptions { Service = "worker" }));
        telemetry.BeginCycle(WorkerOperation.GiftImageCleanup);
        var running = telemetry.Capture();
        _timeProvider.Advance(TimeSpan.FromSeconds(2));

        // Act
        telemetry.RecordTerminalFailure(WorkerOperation.GiftImageCleanup);
        telemetry.CompleteCycle(
            WorkerOperation.GiftImageCleanup,
            TimeSpan.FromHours(24),
            false);
        telemetry.Disable(WorkerOperation.AuthenticationEmailDelivery);
        var completed = telemetry.Capture();

        // Assert
        Assert.Equal(10, completed.Jobs.Count);
        Assert.Equal("running", running.Jobs["GiftImageCleanup"].State);
        Assert.Equal("waiting", completed.Jobs["GiftImageCleanup"].State);
        Assert.Equal(1, completed.Jobs["GiftImageCleanup"].TerminalFailures);
        Assert.Equal("disabled", completed.Jobs["AuthenticationEmailDelivery"].State);
        Assert.Equal(running.BootId, completed.BootId);
        var json = JsonSerializer.Serialize(
            completed,
            JsonSerializerOptions.Web);
        Assert.Contains("\"schemaVersion\":1", json);
        Assert.Contains("\"nextExpectedAt\":null", json);
        Assert.Contains("\"service\":\"worker\"", json);
    }

    [Fact]
    public void RecordHttp_WhenListenerAttached_EmitsNativeMeasurements()
    {
        // Arrange
        using var telemetry = new ApplicationTelemetry(
            _timeProvider,
            Microsoft.Extensions.Options.Options.Create(new ObservabilityOptions()));
        var counts = new List<long>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, meterListener) =>
        {

            if (instrument.Name == "monkado.http.requests")
                meterListener.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => counts.Add(value));
        listener.Start();

        // Act
        telemetry.RecordHttp(
            200,
            TimeSpan.FromMilliseconds(1),
            false);

        // Assert
        Assert.Contains(1, counts);
    }
}
