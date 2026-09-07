using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Queries;

/// <summary>Reads owned export metadata, using the latest request when no identifier is specified.</summary>
/// <param name="memberId">The authenticated member identifier.</param>
/// <param name="exportId">The optional export identifier.</param>
public class GetPersonalDataExportQuery(
    Guid memberId,
    Guid? exportId) : IRequest<PersonalDataExportDetails>
{
    /// <summary>Gets the authenticated member identifier.</summary>
    public Guid MemberId { get; } = memberId;
    /// <summary>Gets the optional export identifier.</summary>
    public Guid? ExportId { get; } = exportId;
}

/// <summary>Reads member-scoped lifecycle metadata.</summary>
/// <param name="service">The member-scoped export service.</param>
/// <param name="logger">The structured operation logger.</param>
public class GetPersonalDataExportQueryHandler(
    IPersonalDataExportService service,
    ILogger<GetPersonalDataExportQueryHandler> logger) : IRequestHandler<GetPersonalDataExportQuery, PersonalDataExportDetails>
{
    /// <inheritdoc/>
    public async Task<PersonalDataExportDetails> Handle(
        GetPersonalDataExportQuery request,
        CancellationToken cancellationToken)
    {
        var export = await service.GetAsync(
            request.MemberId,
            request.ExportId,
            cancellationToken);
        PersonalDataExportLogMessages.Read(
            logger,
            request.MemberId,
            export.Id);

        return export;
    }
}
