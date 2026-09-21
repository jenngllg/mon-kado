using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;

using Microsoft.Extensions.Options;

using System.Diagnostics.Metrics;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

/// <summary>Collects bounded native metrics and consistent snapshots for one process lifetime.</summary>
/// <remarks>Inheritance is prohibited so disposal cannot omit the owned native meter.</remarks>
public sealed class ApplicationTelemetry : IApplicationTelemetry, ITelemetrySnapshotSource, IDisposable
{
    private static readonly double[] _bucketBounds =
    [
        50,
        100,
        250,
        500,
        1000,
        2000,
        5000,
        10000,
        double.PositiveInfinity
    ];
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;
    private readonly string _service;
    private readonly string _bootId = Guid.CreateVersion7().ToString("N");
    private readonly Dictionary<WorkerOperation, WorkerProgress> _jobs;
    private readonly Meter _meter = new("MonKado.Operations");
    private readonly Counter<long> _requests;
    private readonly Histogram<double> _httpDuration;
    private readonly Counter<long> _cycles;
    private readonly Histogram<double> _cycleDuration;
    private readonly Counter<long> _terminalFailures;
    private readonly long[] _buckets = new long[_bucketBounds.Length];
    private long _requestCount;
    private long _serverErrors;
    private long _throttled;
    private long _abandoned;

    /// <summary>Initializes process-local counters; the singleton lifetime preserves cumulative values.</summary>
    /// <param name="timeProvider">The UTC and monotonic clock.</param>
    /// <param name="options">The validated service identity.</param>
    public ApplicationTelemetry(
        TimeProvider timeProvider,
        IOptions<ObservabilityOptions> options)
    {
        _timeProvider = timeProvider;
        _service = options.Value.Service;
        _jobs = Enum.GetValues<WorkerOperation>()
            .ToDictionary(
                operation => operation,
                _ => new WorkerProgress(timeProvider));
        _requests = _meter.CreateCounter<long>("monkado.http.requests");
        _httpDuration = _meter.CreateHistogram<double>(
            "monkado.http.duration",
            "ms");
        _cycles = _meter.CreateCounter<long>("monkado.worker.cycles");
        _cycleDuration = _meter.CreateHistogram<double>(
            "monkado.worker.duration",
            "ms");
        _terminalFailures = _meter.CreateCounter<long>("monkado.worker.terminal_failures");
    }

    /// <inheritdoc />
    public void RecordHttp(
        int statusCode,
        TimeSpan elapsed,
        bool abandoned)
    {
        var milliseconds = Math.Max(
            0,
            elapsed.TotalMilliseconds);
        var status = Math.Clamp(
            statusCode / 100,
            1,
            5);
        lock (_gate)
        {
            _requestCount++;
            _serverErrors += statusCode >= 500 && !abandoned ? 1 : 0;
            _throttled += statusCode == 429 ? 1 : 0;
            _abandoned += abandoned ? 1 : 0;
            _buckets[Array.FindIndex(
                _bucketBounds,
                bound => milliseconds <= bound)]++;
        }
        _requests.Add(
            1,
            new KeyValuePair<string, object?>(
                "status_class",
                status),
            new KeyValuePair<string, object?>(
                "abandoned",
                abandoned),
            new KeyValuePair<string, object?>(
                "throttled",
                statusCode == 429));
        _httpDuration.Record(milliseconds);
    }

    /// <inheritdoc />
    public void BeginCycle(WorkerOperation operation)
    {
        lock (_gate)
        {
            _jobs[operation].Begin();
        }
    }

    /// <inheritdoc />
    public void CompleteCycle(
        WorkerOperation operation,
        TimeSpan nextDelay,
        bool successful)
    {
        double elapsed;
        lock (_gate)
        {
            elapsed = _jobs[operation].Complete(
                nextDelay,
                successful);
        }
        var tag = new KeyValuePair<string, object?>(
            "operation",
            operation.ToString());
        _cycles.Add(
            1,
            tag,
            new KeyValuePair<string, object?>(
                "successful",
                successful));
        _cycleDuration.Record(
            elapsed,
            tag);
    }

    /// <inheritdoc />
    public void Disable(WorkerOperation operation)
    {
        lock (_gate)
        {
            _jobs[operation].Disable();
        }
    }

    /// <inheritdoc />
    public void RecordTerminalFailure(WorkerOperation operation)
    {
        lock (_gate)
        {
            _jobs[operation].RecordTerminalFailure();
        }
        _terminalFailures.Add(
            1,
            new KeyValuePair<string, object?>(
                "operation",
                operation.ToString()));
    }

    /// <inheritdoc />
    public ApplicationTelemetrySnapshot Capture()
    {
        lock (_gate)
        {

            return new ApplicationTelemetrySnapshot
            {
                Service = _service,
                BootId = _bootId,
                CreatedAt = _timeProvider.GetUtcNow().UtcDateTime,
                Http = new HttpTelemetrySnapshot
                {
                    Requests = _requestCount,
                    ServerErrors = _serverErrors,
                    Throttled = _throttled,
                    Abandoned = _abandoned,
                    DurationBuckets = [.. _buckets]
                },
                Jobs = _service == "worker"
                    ? _jobs.ToDictionary(
                        pair => pair.Key.ToString(),
                        pair => pair.Value.Capture())
                    : new Dictionary<string, WorkerTelemetrySnapshot>()
            };
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _meter.Dispose();
        GC.SuppressFinalize(this);
    }
}
