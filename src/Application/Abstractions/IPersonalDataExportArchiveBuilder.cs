using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Builds a private complete archive from one personal-data snapshot.</summary>
public interface IPersonalDataExportArchiveBuilder
{
    /// <summary>Writes an immutable attempt and verifies every included image against its snapshot digest.</summary>
    /// <param name="workItem">The leased export and its member.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The snapshot date and complete archive length.</returns>
    Task<PersonalDataExportArchive> BuildAsync(
        PersonalDataExportWorkItem workItem,
        CancellationToken cancellationToken);
}
