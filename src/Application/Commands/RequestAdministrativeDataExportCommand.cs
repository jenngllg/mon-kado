using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Requests an export through the explicitly authorized administrative path.</summary>
/// <param name="administratorId">The authenticated actor.</param>
/// <param name="memberId">The target account.</param>
/// <param name="requestReference">The required external reference.</param>
public class RequestAdministrativeDataExportCommand(
    Guid administratorId,
    Guid? memberId,
    string? requestReference) : IRequest<PersonalDataExportDetails>
{
    /// <summary>Gets the authenticated actor.</summary>
    public Guid AdministratorId { get; } = administratorId;
    /// <summary>Gets the target account.</summary>
    public Guid? MemberId { get; } = memberId;
    /// <summary>Gets the required external request reference.</summary>
    public string? RequestReference { get; } = requestReference;
}

/// <summary>Coordinates the validated administrative operation without logging client-supplied reference text.</summary>
/// <param name="service">The administrative export service.</param>
/// <param name="logger">The correlated operation logger.</param>
public class RequestAdministrativeDataExportCommandHandler(
    IAdministrativeDataExportService service,
    ILogger<RequestAdministrativeDataExportCommandHandler> logger) : IRequestHandler<RequestAdministrativeDataExportCommand, PersonalDataExportDetails>
{
    /// <inheritdoc/>
    public async Task<PersonalDataExportDetails> Handle(
        RequestAdministrativeDataExportCommand request,
        CancellationToken cancellationToken)
    {
        var result = await service.RequestAsync(
            request.AdministratorId,
            request.MemberId.GetValueOrDefault(),
            request.RequestReference!.Trim(),
            cancellationToken);
        AdministrativeDataExportLogMessages.Requested(
            logger,
            request.AdministratorId,
            request.MemberId.GetValueOrDefault(),
            result.Id);

        return result;
    }
}
