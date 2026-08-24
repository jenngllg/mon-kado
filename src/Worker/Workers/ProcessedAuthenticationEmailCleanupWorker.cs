using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Worker.Logging;
using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.Workers;

/// <summary>
/// Removes processed authentication emails after their retention period.
/// </summary>
public sealed class ProcessedAuthenticationEmailCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProcessedAuthenticationEmailCleanupWorker> _logger;
    private readonly AuthenticationCleanupOptions _cleanupOptions;
    private readonly TimeSpan _retention;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessedAuthenticationEmailCleanupWorker" /> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="options">The authentication email options.</param>
    /// <param name="cleanupOptions">The shared cleanup options.</param>
    /// <param name="logger">The logger.</param>
    public ProcessedAuthenticationEmailCleanupWorker(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        IOptions<AuthenticationEmailOptions> options,
        IOptions<AuthenticationCleanupOptions> cleanupOptions,
        ILogger<ProcessedAuthenticationEmailCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _cleanupOptions = cleanupOptions.Value;
        _retention = TimeSpan.FromDays(options.Value.ProcessedRetentionDays);
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                TimeSpan nextDelay;
                using (WorkerLogScope.Begin(
                    _logger,
                    "ProcessedAuthenticationEmailCleanup"))
                {
                    nextDelay = await CleanupOnceAsync(stoppingToken);
                }
                await Task.Delay(
                    nextDelay,
                    _timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Runs one cleanup cycle and determines the next delay.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The delay before the next cleanup cycle.</returns>
    private async Task<TimeSpan> CleanupOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var deletedCount = await DeleteProcessedEmailsAsync(cancellationToken);

            if (deletedCount > 0)
            {
                WorkerLogMessages.ProcessedAuthenticationEmailsDeleted(
                    _logger,
                    deletedCount);
            }

            return _cleanupOptions.Interval;
        }
        catch (OperationCanceledException)
        {

            throw;
        }
        catch (Exception exception)
        {
            WorkerLogMessages.ProcessedAuthenticationEmailCleanupFailed(
                _logger,
                exception.GetType().Name,
                exception);

            return _cleanupOptions.FailureRetryInterval;
        }
    }

    /// <summary>
    /// Deletes all eligible messages in bounded batches.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The total number of deleted messages.</returns>
    private async Task<int> DeleteProcessedEmailsAsync(CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - _retention;
        var totalDeleted = 0;
        int deletedInBatch;

        do
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var cleanup = scope.ServiceProvider
                .GetRequiredService<IProcessedAuthenticationEmailCleanup>();
            deletedInBatch = await cleanup.DeleteProcessedEmailsAsync(
                cutoff,
                _cleanupOptions.BatchSize,
                cancellationToken);
            totalDeleted += deletedInBatch;
        }
        while (deletedInBatch == _cleanupOptions.BatchSize);

        return totalDeleted;
    }
}
