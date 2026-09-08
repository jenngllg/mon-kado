using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;

/// <summary>Opens private archives while keeping their final authorization at the caller's trust boundary.</summary>
public interface IPersonalDataExportArchiveReader
{
    /// <summary>Validates storage and lifetime, then authorizes release before transferring ownership of the stream.</summary>
    /// <param name="dataExport">The previously authorized export row.</param>
    /// <param name="authorizeReleaseAsync">The mandatory final authorization and optional durable audit operation.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The caller-owned stream after authorization succeeds.</returns>
    Task<PersonalDataExportDownload> OpenAsync(
        MemberDataExport dataExport,
        Func<CancellationToken, Task> authorizeReleaseAsync,
        CancellationToken cancellationToken);
}
