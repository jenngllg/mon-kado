using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Requests irreversible deletion of the authenticated account.</summary>
/// <param name="memberId">The authenticated member identifier.</param>
public class RequestMemberAccountDeletionCommand(Guid memberId) : IRequest
{
    /// <summary>Gets the authenticated member identifier.</summary>
    public Guid MemberId { get; } = memberId;
}

/// <summary>Coordinates the account deletion request.</summary>
/// <param name="service">The account deletion service.</param>
/// <param name="logger">The logger.</param>
public class RequestMemberAccountDeletionCommandHandler(
    IMemberAccountDeletionService service,
    ILogger<RequestMemberAccountDeletionCommandHandler> logger) : IRequestHandler<RequestMemberAccountDeletionCommand>
{
    /// <inheritdoc/>
    public async Task Handle(
        RequestMemberAccountDeletionCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.AccountDeletionRequestStarted(
            logger,
            request.MemberId);
        await service.RequestAsync(
            request.MemberId,
            cancellationToken);
        ApplicationLogMessages.AccountDeletionRequested(
            logger,
            request.MemberId);
    }
}
