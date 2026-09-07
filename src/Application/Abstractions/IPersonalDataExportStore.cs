using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Defines private, bounded and atomic archive storage on a shared local volume.</summary>
public interface IPersonalDataExportStore
{
    /// <summary>Initializes and probes the private volume at host startup.</summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the startup storage check.</returns>
    Task InitializeAsync(CancellationToken cancellationToken);
    /// <summary>Writes and atomically publishes an immutable attempt while holding a cross-process write lock.</summary>
    /// <param name="exportId">The generated export identifier.</param>
    /// <param name="archiveId">The generated attempt identifier.</param>
    /// <param name="writeAsync">The streaming archive producer.</param>
    /// <param name="maximumBytes">The maximum complete archive length.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The complete immutable archive length.</returns>
    Task<long> WriteAsync(
        Guid exportId,
        Guid archiveId,
        Func<PersonalDataExportWriteContext, CancellationToken, Task> writeAsync,
        long maximumBytes,
        CancellationToken cancellationToken);
    /// <summary>Opens a previously authorized immutable archive.</summary>
    /// <param name="exportId">The export identifier.</param>
    /// <param name="archiveId">The successful attempt identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The caller-owned read stream, or null if the archive is absent.</returns>
    Task<Stream?> OpenReadAsync(
        Guid exportId,
        Guid archiveId,
        CancellationToken cancellationToken);
    /// <summary>Deletes one export's files without racing an active writer.</summary>
    /// <param name="exportId">The generated export identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>False while a writer or reader prevents deletion; otherwise true after idempotent cleanup.</returns>
    Task<bool> DeleteAsync(
        Guid exportId,
        CancellationToken cancellationToken);
    /// <summary>Reconciles bounded old attempts against durable ownership and fencing state.</summary>
    /// <param name="cutoff">The inclusive UTC abandoned-file cutoff.</param>
    /// <param name="batchSize">The maximum number of attempts examined.</param>
    /// <param name="isReferencedAsync">Checks that an attempt is still published or actively leased.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the bounded reconciliation cycle.</returns>
    Task ReconcileAsync(
        DateTime cutoff,
        int batchSize,
        Func<Guid, Guid, CancellationToken, Task<bool>> isReferencedAsync,
        CancellationToken cancellationToken);
}
