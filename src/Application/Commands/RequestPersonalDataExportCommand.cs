using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Requests or reuses the current member's personal-data export.</summary>
/// <param name="memberId">The authenticated member identifier.</param>
public class RequestPersonalDataExportCommand(Guid memberId) : IRequest<PersonalDataExportDetails>
{
    /// <summary>Gets the authenticated member identifier.</summary>
    public Guid MemberId { get; } = memberId;
}

/// <summary>Coordinates an export request without exposing an owner supplied by the client.</summary>
/// <param name="service">The member-scoped export service.</param>
/// <param name="logger">The structured operation logger.</param>
public class RequestPersonalDataExportCommandHandler(
    IPersonalDataExportService service,
    ILogger<RequestPersonalDataExportCommandHandler> logger) : IRequestHandler<RequestPersonalDataExportCommand, PersonalDataExportDetails>
{
    /// <inheritdoc/>
    public async Task<PersonalDataExportDetails> Handle(
        RequestPersonalDataExportCommand request,
        CancellationToken cancellationToken)
    {
        var export = await service.RequestAsync(
            request.MemberId,
            cancellationToken);
        PersonalDataExportLogMessages.Requested(
            logger,
            request.MemberId,
            export.Id);

        return export;
    }
}
