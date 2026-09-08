using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Executes a verified support request after explicit target confirmation.</summary>
/// <param name="administratorId">The authenticated actor.</param>
/// <param name="memberId">The route target.</param>
/// <param name="confirmedMemberId">The explicitly confirmed target.</param>
/// <param name="requestReference">The external request reference.</param>
public class ExecuteAdministrativeAccountErasureCommand(
    Guid administratorId,
    Guid? memberId,
    Guid? confirmedMemberId,
    string? requestReference) : IRequest
{
    /// <summary>Gets the authenticated actor.</summary>
    public Guid AdministratorId { get; } = administratorId;
    /// <summary>Gets the target account.</summary>
    public Guid? MemberId { get; } = memberId;
    /// <summary>Gets the explicitly confirmed target.</summary>
    public Guid? ConfirmedMemberId { get; } = confirmedMemberId;
    /// <summary>Gets the external reference supplied for centralized validation.</summary>
    public string? RequestReference { get; } = requestReference;
}

/// <summary>Coordinates validated erasure without exposing client text in logs.</summary>
/// <param name="service">The transactional erasure service.</param>
/// <param name="logger">The correlated logger.</param>
public class ExecuteAdministrativeAccountErasureCommandHandler(
    IAdministrativeAccountErasureService service,
    ILogger<ExecuteAdministrativeAccountErasureCommandHandler> logger) : IRequestHandler<ExecuteAdministrativeAccountErasureCommand>
{
    /// <inheritdoc/>
    public async Task Handle(
        ExecuteAdministrativeAccountErasureCommand request,
        CancellationToken cancellationToken)
    {
        var operationId = await service.ExecuteAsync(
            request.AdministratorId,
            request.MemberId.GetValueOrDefault(),
            request.RequestReference!.Trim(), // The shared FluentValidation pipeline requires this value.
            cancellationToken);
        AdministrativeAccountErasureLogMessages.Executed(
            logger,
            request.AdministratorId,
            request.MemberId.GetValueOrDefault(),
            operationId);
    }
}
