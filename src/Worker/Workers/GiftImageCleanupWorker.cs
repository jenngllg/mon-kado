using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Worker.Logging;
using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.Workers;

/// <summary>
/// Deletes obsolete images and reconciles abandoned pending image writes.
/// </summary>
public sealed class GiftImageCleanupWorker : BackgroundService
{
    private static readonly TimeSpan _firstRetryDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGiftImageStore _store;
    private readonly TimeProvider _timeProvider;
    private readonly GiftImageCleanupOptions _options;
    private readonly ILogger<GiftImageCleanupWorker> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GiftImageCleanupWorker" /> class.
    /// </summary>
    /// <param name="scopeFactory">The service scope factory.</param>
    /// <param name="store">The shared durable image store.</param>
    /// <param name="timeProvider">The time provider.</param>
    /// <param name="options">The cleanup options.</param>
    /// <param name="logger">The logger.</param>
    public GiftImageCleanupWorker(
        IServiceScopeFactory scopeFactory,
        IGiftImageStore store,
        TimeProvider timeProvider,
        IOptions<GiftImageCleanupOptions> options,
        ILogger<GiftImageCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _store = store;
        _timeProvider = timeProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                var nextDelay = await CleanupOnceAsync(stoppingToken);
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
    /// Processes one bounded cleanup cycle and selects the next delay.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The delay before the next cycle.</returns>
    private async Task<TimeSpan> CleanupOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            using (WorkerLogScope.Begin(
                _logger,
                "GiftImageCleanup"))
            {
                await DeleteObsoleteImagesAsync(cancellationToken);
                await ReconcilePendingImagesAsync(cancellationToken);
            }

            return _options.Interval;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            WorkerLogMessages.GiftImageCleanupFailed(
                _logger,
                exception.GetType().Name,
                exception);

            return _options.FailureRetryInterval;
        }
    }

    /// <summary>
    /// Processes a bounded batch of durable obsolete-image deletions.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous cleanup.</returns>
    private async Task DeleteObsoleteImagesAsync(CancellationToken cancellationToken)
    {
        var processedCount = 0;

        while (processedCount < _options.BatchSize)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var cleanupService = scope.ServiceProvider.GetRequiredService<IGiftImageCleanupService>();
            var deletion = await cleanupService.ClaimNextAsync(
                _timeProvider.GetUtcNow().UtcDateTime,
                _options.LeaseDuration,
                cancellationToken);

            if (deletion is null)
                return;

            await DeleteOrRescheduleAsync(
                cleanupService,
                deletion,
                cancellationToken);
            processedCount++;
        }
    }

    /// <summary>
    /// Deletes one obsolete image or durably schedules its next attempt.
    /// </summary>
    /// <param name="cleanupService">The scoped durable cleanup service.</param>
    /// <param name="deletion">The claimed deletion.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous attempt.</returns>
    private async Task DeleteOrRescheduleAsync(
        IGiftImageCleanupService cleanupService,
        GiftImageDeletion deletion,
        CancellationToken cancellationToken)
    {
        try
        {
            await _store.DeleteAsync(
                deletion.ImageId,
                cancellationToken);
            await cleanupService.CompleteAsync(
                deletion.Id,
                cancellationToken);
            WorkerLogMessages.GiftImageDeleted(
                _logger,
                deletion.ImageId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            var retryDelay = GetRetryDelay(deletion.AttemptCount);
            await cleanupService.ScheduleRetryAsync(
                deletion.Id,
                _timeProvider.GetUtcNow().UtcDateTime.Add(retryDelay),
                cancellationToken);

            throw;
        }
    }

    /// <summary>
    /// Reconciles aged pending markers against current PostgreSQL references.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous reconciliation.</returns>
    private async Task ReconcilePendingImagesAsync(CancellationToken cancellationToken)
    {
        var cutoff = _timeProvider.GetUtcNow().UtcDateTime - _options.PendingGracePeriod;
        var pendingImages = await _store.GetPendingAsync(
            cutoff,
            _options.BatchSize,
            cancellationToken);

        foreach (var pendingImage in pendingImages)
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var cleanupService = scope.ServiceProvider.GetRequiredService<IGiftImageCleanupService>();
            var isReferenced = await cleanupService.IsReferencedAsync(
                pendingImage.ImageId,
                cancellationToken);

            if (isReferenced)
            {
                await _store.MarkCommittedAsync(
                    pendingImage.ImageId,
                    cancellationToken);
            }
            else
            {
                await _store.DeleteAsync(
                    pendingImage.ImageId,
                    cancellationToken);
            }

            WorkerLogMessages.PendingGiftImageReconciled(
                _logger,
                pendingImage.ImageId,
                isReferenced);
        }
    }

    /// <summary>
    /// Calculates a bounded exponential retry delay.
    /// </summary>
    /// <param name="attemptCount">The current attempt count.</param>
    /// <returns>The delay before the next deletion attempt.</returns>
    private TimeSpan GetRetryDelay(int attemptCount)
    {
        var exponent = Math.Min(
            Math.Max(
                0,
                attemptCount - 1),
            20);
        var delayTicks = Math.Min(
            _firstRetryDelay.Ticks * Math.Pow(
                2,
                exponent),
            _options.MaximumRetryDelay.Ticks);

        return TimeSpan.FromTicks((long)delayTicks);
    }
}
