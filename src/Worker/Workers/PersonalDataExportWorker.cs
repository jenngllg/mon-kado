using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Options;
using JennGllg.Fr.MonKado.Back.Worker.Logging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Worker.Workers;

/// <summary>Generates one private archive at a time while independently renewing its fenced database lease.</summary>
/// <param name="scopeFactory">The independent database scope factory.</param>
/// <param name="timeProvider">The controlled deadline and scheduling clock.</param>
/// <param name="options">The validated worker limits.</param>
/// <param name="logger">The structured lifecycle logger.</param>
public class PersonalDataExportWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider timeProvider,
    IOptions<PersonalDataExportOptions> options,
    ILogger<PersonalDataExportWorker> logger) : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = await RunCycleAsync(stoppingToken);
                await Task.Delay(
                    delay,
                    timeProvider,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    /// <summary>Runs bounded retention and claims at most one generation for this worker instance.</summary>
    /// <param name="cancellationToken">The host shutdown token.</param>
    /// <returns>The next cycle delay.</returns>
    private async Task<TimeSpan> RunCycleAsync(CancellationToken cancellationToken)
    {
        using var logScope = WorkerLogScope.Begin(
            logger,
            "PersonalDataExport");
        try
        {
            await ProcessNextAsync(cancellationToken);

            return options.Value.PollInterval;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {

            throw;
        }
        catch (Exception exception)
        {
            PersonalDataExportLogMessages.CycleFailed(
                logger,
                ClassifyFailure(exception));

            return options.Value.FailureInterval;
        }
    }

    /// <summary>Disposes the polling database scope before the worker schedules its next cycle.</summary>
    /// <param name="cancellationToken">The host shutdown token.</param>
    /// <returns>A task representing cleanup and at most one generation.</returns>
    private async Task ProcessNextAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
        await jobs.CleanupAsync(cancellationToken);
        var workItem = await jobs.ClaimAsync(cancellationToken);

        if (workItem is not null)
            await GenerateAsync(
                workItem,
                cancellationToken);
    }

    /// <summary>Builds and publishes only while this attempt remains live, without sharing its snapshot context with renewal.</summary>
    /// <param name="workItem">The fenced generation attempt.</param>
    /// <param name="stoppingToken">The host shutdown token.</param>
    /// <returns>A task representing the attempt and its durable outcome acknowledgement.</returns>
    private async Task GenerateAsync(
        PersonalDataExportWorkItem workItem,
        CancellationToken stoppingToken)
    {
        using var deadline = new CancellationTokenSource(
            options.Value.AttemptTimeout,
            timeProvider);
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            deadline.Token);
        using var renewalStop = CancellationTokenSource.CreateLinkedTokenSource(attempt.Token);
        var renewal = RenewLeaseAsync(
            workItem,
            attempt,
            renewalStop.Token);
        PersonalDataExportLogMessages.AttemptStarted(
            logger,
            workItem.ExportId);
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var builder = scope.ServiceProvider.GetRequiredService<IPersonalDataExportArchiveBuilder>();
            var archive = await builder.BuildAsync(
                workItem,
                attempt.Token);
            await renewalStop.CancelAsync();
            await renewal;
            attempt.Token.ThrowIfCancellationRequested();
            await using var publicationScope = scopeFactory.CreateAsyncScope();
            var jobs = publicationScope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();

            if (!await jobs.RenewAsync(
                workItem,
                attempt.Token) || !await jobs.CompleteAsync(
                workItem,
                archive,
                attempt.Token))
                throw new PersonalDataExportLeaseLostException();
            PersonalDataExportLogMessages.Ready(
                logger,
                workItem.ExportId);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {

            throw;
        }
        catch (Exception exception)
        {
            await renewalStop.CancelAsync();
            await renewal;
            PersonalDataExportLogMessages.AttemptFailed(
                logger,
                workItem.ExportId,
                ClassifyFailure(exception));
            await using var failureScope = scopeFactory.CreateAsyncScope();
            var jobs = failureScope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();
            await jobs.FailAsync(
                workItem,
                exception is PersonalDataExportTooLargeException ? PersonalDataExportFailure.TooLarge : PersonalDataExportFailure.GenerationFailed,
                stoppingToken);
        }
        finally
        {
            await renewalStop.CancelAsync();
            await renewal;
        }
    }

    /// <summary>Renews with a separate context and cancels generation as soon as ownership cannot be confirmed.</summary>
    /// <param name="workItem">The fenced generation attempt.</param>
    /// <param name="attempt">The generation cancellation source.</param>
    /// <param name="cancellationToken">The renewal stop token.</param>
    /// <returns>A task that always observes its renewal failures locally.</returns>
    private async Task RenewLeaseAsync(
        PersonalDataExportWorkItem workItem,
        CancellationTokenSource attempt,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(
                options.Value.LeaseRenewalInterval,
                timeProvider);
            // This method owns the timer; cancellation, not external disposal, terminates renewal.
            while (true)
            {
                await timer.WaitForNextTickAsync(cancellationToken);
                await using var scope = scopeFactory.CreateAsyncScope();
                var jobs = scope.ServiceProvider.GetRequiredService<IPersonalDataExportJobs>();

                if (!await jobs.RenewAsync(
                    workItem,
                    cancellationToken))
                {
                    await attempt.CancelAsync();

                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Stopping renewal is expected after generation or host shutdown.
        }
        catch (Exception)
        {
            // The attempt boundary records one bounded failure, never a raw storage exception.
            await attempt.CancelAsync();
        }
    }

    /// <summary>Reduces failures to a fixed allowlist without including exception names, messages or nested paths.</summary>
    /// <param name="exception">The failed operation exception.</param>
    /// <returns>The bounded technical classification.</returns>
    private static string ClassifyFailure(Exception exception)
    {

        return exception switch
        {
            PersonalDataExportTooLargeException => "TOO_LARGE",
            PersonalDataExportLeaseLostException => "LEASE_LOST",
            PersonalDataExportStorageUnavailableException or GiftImageStorageUnavailableException or IOException or UnauthorizedAccessException => "STORAGE_UNAVAILABLE",
            DependencyUnavailableException => "DEPENDENCY_UNAVAILABLE",
            OperationCanceledException => "ATTEMPT_CANCELLED",
            InvalidAuthenticationSessionException => "MEMBER_UNAVAILABLE",
            _ => "GENERATION_FAILED"
        };
    }
}
