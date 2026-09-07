namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Streams an explicit member-only database snapshot and its private image-reference manifest.</summary>
public interface IPersonalDataExportSnapshotReader
{
    /// <summary>Writes all retained exportable data within one repeatable-read database snapshot.</summary>
    /// <param name="memberId">The trusted member identifier.</param>
    /// <param name="dataDestination">The caller-owned JSON destination.</param>
    /// <param name="imageManifest">The caller-owned seekable disk-backed manifest destination.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The UTC start of the successfully read snapshot.</returns>
    Task<DateTime> WriteAsync(
        Guid memberId,
        Stream dataDestination,
        Stream imageManifest,
        CancellationToken cancellationToken);
}
