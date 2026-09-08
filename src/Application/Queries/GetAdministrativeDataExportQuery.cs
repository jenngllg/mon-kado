using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Gets an export through the explicitly authorized administrative path.</summary>
/// <param name="administratorId">The authenticated actor.</param>
/// <param name="memberId">The target account.</param>
/// <param name="exportId">The requested export identifier.</param>
public class GetAdministrativeDataExportQuery(
    Guid administratorId,
    Guid? memberId,
    Guid? exportId) : IRequest<PersonalDataExportDetails>
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
public class GetAdministrativeDataExportQueryHandler(
    IAdministrativeDataExportService service,
    ILogger<GetAdministrativeDataExportQueryHandler> logger) : IRequestHandler<GetAdministrativeDataExportQuery, PersonalDataExportDetails>
{
    /// <inheritdoc/>
    public async Task<PersonalDataExportDetails> Handle(
        GetAdministrativeDataExportQuery request,
        CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(
            request.AdministratorId,
            request.MemberId.GetValueOrDefault(),
            request.ExportId,
            cancellationToken);
        AdministrativeDataExportLogMessages.Read(
            logger,
            request.AdministratorId,
            request.MemberId.GetValueOrDefault(),
            result.Id);

        return result;
    }
}
