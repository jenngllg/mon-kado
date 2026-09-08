using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Downloads an export through the explicitly authorized administrative path.</summary>
/// <param name="administratorId">The authenticated actor.</param>
/// <param name="memberId">The target account.</param>
/// <param name="exportId">The requested export identifier.</param>
public class DownloadAdministrativeDataExportQuery(
    Guid administratorId,
    Guid? memberId,
    Guid? exportId) : IRequest<PersonalDataExportDownload>
{
    /// <summary>Gets the authenticated actor.</summary>
    public Guid AdministratorId { get; } = administratorId;
    /// <summary>Gets the target account.</summary>
    public Guid? MemberId { get; } = memberId;
    /// <summary>Gets the requested export identifier.</summary>
    public Guid? ExportId { get; } = exportId;
}

/// <summary>Coordinates the validated administrative operation without logging client-supplied reference text.</summary>
/// <param name="service">The administrative export service.</param>
/// <param name="logger">The correlated operation logger.</param>
public class DownloadAdministrativeDataExportQueryHandler(
    IAdministrativeDataExportService service,
    ILogger<DownloadAdministrativeDataExportQueryHandler> logger) : IRequestHandler<DownloadAdministrativeDataExportQuery, PersonalDataExportDownload>
{
    /// <inheritdoc/>
    public async Task<PersonalDataExportDownload> Handle(
        DownloadAdministrativeDataExportQuery request,
        CancellationToken cancellationToken)
    {
        var result = await service.OpenArchiveAsync(
            request.AdministratorId,
            request.MemberId.GetValueOrDefault(),
            request.ExportId.GetValueOrDefault(),
            cancellationToken);
        AdministrativeDataExportLogMessages.DownloadStarted(
            logger,
            request.AdministratorId,
            request.MemberId.GetValueOrDefault(),
            result.ExportId);

        return result;
    }
}
