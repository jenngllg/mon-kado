using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Confirms irreversible deletion of the authenticated account.</summary>
/// <param name="memberId">The authenticated member identifier.</param>
/// <param name="token">The protected email confirmation token.</param>
public class ConfirmMemberAccountDeletionCommand(
    Guid memberId,
    string? token) : IRequest
{
    /// <summary>Gets the authenticated member identifier.</summary>
    public Guid MemberId { get; } = memberId;
    /// <summary>Gets the protected confirmation token.</summary>
    public string? Token { get; } = token;
}

/// <summary>Coordinates the account deletion confirmation.</summary>
/// <param name="service">The account deletion service.</param>
/// <param name="logger">The logger.</param>
public class ConfirmMemberAccountDeletionCommandHandler(
    IMemberAccountDeletionService service,
    ILogger<ConfirmMemberAccountDeletionCommandHandler> logger) : IRequestHandler<ConfirmMemberAccountDeletionCommand>
{
    /// <inheritdoc/>
    public async Task Handle(
        ConfirmMemberAccountDeletionCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.AccountDeletionStarted(
            logger,
            request.MemberId);
        await service.ConfirmAsync(
            request.MemberId,
            request.Token ?? string.Empty,
            cancellationToken);
        ApplicationLogMessages.AccountDeleted(
            logger,
            request.MemberId);
    }
}
