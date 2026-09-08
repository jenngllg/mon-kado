using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Provides explicitly authorized and audited administrative access to member exports.</summary>
public interface IAdministrativeDataExportService
{
    /// <summary>Creates or reuses an export and durably records its administrative purpose.</summary>
    /// <param name="administratorId">The authenticated actor.</param>
    /// <param name="memberId">The target account.</param>
    /// <param name="requestReference">The validated request reference.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The export lifecycle metadata.</returns>
    Task<PersonalDataExportDetails> RequestAsync(
        Guid administratorId,
        Guid memberId,
        string requestReference,
        CancellationToken cancellationToken);
    /// <summary>Reads an administratively requested export or the most recently requested retained export.</summary>
    /// <param name="administratorId">The authenticated actor.</param>
    /// <param name="memberId">The target account.</param>
    /// <param name="exportId">The export identifier, or null for the latest administrative request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The export lifecycle metadata.</returns>
    Task<PersonalDataExportDetails> GetAsync(
        Guid administratorId,
        Guid memberId,
        Guid? exportId,
        CancellationToken cancellationToken);
    /// <summary>Rechecks authorization and records an audit event before releasing an archive stream.</summary>
    /// <param name="administratorId">The authenticated actor.</param>
    /// <param name="memberId">The target account.</param>
    /// <param name="exportId">The administratively requested export.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The caller-owned archive stream.</returns>
    Task<PersonalDataExportDownload> OpenArchiveAsync(
        Guid administratorId,
        Guid memberId,
        Guid exportId,
        CancellationToken cancellationToken);
}
