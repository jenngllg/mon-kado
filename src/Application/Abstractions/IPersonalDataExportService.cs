using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Manages authenticated member export requests and authorized downloads.</summary>
public interface IPersonalDataExportService
{
    /// <summary>Creates one request or reuses its ongoing or unexpired predecessor.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The durable request metadata.</returns>
    Task<PersonalDataExportDetails> RequestAsync(
        Guid memberId,
        CancellationToken cancellationToken);
    /// <summary>Reads one owned request, or the latest retained request when no identifier is supplied.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="exportId">The requested identifier, or null for the latest request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The owned request metadata.</returns>
    Task<PersonalDataExportDetails> GetAsync(
        Guid memberId,
        Guid? exportId,
        CancellationToken cancellationToken);
    /// <summary>Opens a ready, unexpired archive after checking the current account.</summary>
    /// <param name="memberId">The authenticated member identifier.</param>
    /// <param name="exportId">The requested archive's export identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A caller-owned archive stream and its public identifier.</returns>
    Task<PersonalDataExportDownload> OpenArchiveAsync(
        Guid memberId,
        Guid exportId,
        CancellationToken cancellationToken);
}
