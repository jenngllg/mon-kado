using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Opens an owned archive after rechecking its account and absolute expiration.</summary>
/// <param name="memberId">The authenticated member identifier.</param>
/// <param name="exportId">The requested export identifier.</param>
public class DownloadPersonalDataExportQuery(
    Guid memberId,
    Guid? exportId) : IRequest<PersonalDataExportDownload>
{
    /// <summary>Gets the authenticated member identifier.</summary>
    public Guid MemberId { get; } = memberId;
    /// <summary>Gets the requested export identifier.</summary>
    public Guid? ExportId { get; } = exportId;
}

/// <summary>Coordinates authenticated archive access without creating public links.</summary>
/// <param name="service">The member-scoped export service.</param>
/// <param name="logger">The structured operation logger.</param>
public class DownloadPersonalDataExportQueryHandler(
    IPersonalDataExportService service,
    ILogger<DownloadPersonalDataExportQueryHandler> logger) : IRequestHandler<DownloadPersonalDataExportQuery, PersonalDataExportDownload>
{
    /// <inheritdoc/>
    public async Task<PersonalDataExportDownload> Handle(
        DownloadPersonalDataExportQuery request,
        CancellationToken cancellationToken)
    {
        var archive = await service.OpenArchiveAsync(
            request.MemberId,
            request.ExportId.GetValueOrDefault(),
            cancellationToken);
        PersonalDataExportLogMessages.DownloadStarted(
            logger,
            request.MemberId,
            archive.ExportId);

        return archive;
    }
}
