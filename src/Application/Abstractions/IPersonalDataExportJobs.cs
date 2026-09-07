using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Coordinates durable export attempts, fencing, publication and retention.</summary>
public interface IPersonalDataExportJobs
{
    /// <summary>Claims one eligible generation attempt without waiting for another worker.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The fenced work item, or null when no attempt was claimed.</returns>
    Task<PersonalDataExportWorkItem?> ClaimAsync(CancellationToken cancellationToken);
    /// <summary>Renews only the current live attempt.</summary>
    /// <param name="workItem">The claimed work item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether the worker still owns the lease.</returns>
    Task<bool> RenewAsync(
        PersonalDataExportWorkItem workItem,
        CancellationToken cancellationToken);
    /// <summary>Publishes a complete archive and its ready notification atomically.</summary>
    /// <param name="workItem">The claimed work item.</param>
    /// <param name="archive">The completely written archive.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Whether publication is confirmed for this attempt.</returns>
    Task<bool> CompleteAsync(
        PersonalDataExportWorkItem workItem,
        PersonalDataExportArchive archive,
        CancellationToken cancellationToken);
    /// <summary>Schedules a fresh snapshot retry or records a terminal failure.</summary>
    /// <param name="workItem">The failed work item.</param>
    /// <param name="failure">The bounded failure classification.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the fenced acknowledgement.</returns>
    Task FailAsync(
        PersonalDataExportWorkItem workItem,
        PersonalDataExportFailure failure,
        CancellationToken cancellationToken);
    /// <summary>Cleans a bounded batch of unavailable archives and reconciles abandoned attempts.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the retention cycle.</returns>
    Task CleanupAsync(CancellationToken cancellationToken);
}
