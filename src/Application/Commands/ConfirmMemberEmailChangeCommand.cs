using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents a request to confirm a member email change.
/// </summary>
/// <param name="requestId">The email change request identifier.</param>
/// <param name="token">The encoded confirmation token.</param>
public class ConfirmMemberEmailChangeCommand(
    Guid? requestId,
    string? token) : IRequest, IGenericValidationFailure
{
    /// <summary>
    /// Gets the email change request identifier.
    /// </summary>
    public Guid? RequestId { get; } = requestId;

    /// <summary>
    /// Gets the encoded confirmation token.
    /// </summary>
    public string? Token { get; } = token;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        return new MemberEmailChangeInvalidException();
    }
}

/// <summary>
/// Handles member email change confirmations.
/// </summary>
/// <param name="memberEmailChangeService">The member email change service.</param>
/// <param name="logger">The logger.</param>
public class ConfirmMemberEmailChangeCommandHandler(
    IMemberEmailChangeService memberEmailChangeService,
    ILogger<ConfirmMemberEmailChangeCommandHandler> logger)
    : IRequestHandler<ConfirmMemberEmailChangeCommand>
{
    /// <summary>
    /// Confirms a pending member email change.
    /// </summary>
    /// <param name="request">The email change confirmation command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task Handle(
        ConfirmMemberEmailChangeCommand request,
        CancellationToken cancellationToken)
    {
        var requestId = request.RequestId ?? Guid.Empty;
        ApplicationLogMessages.MemberEmailChangeConfirmationStarted(
            logger,
            requestId);
        var confirmed = await memberEmailChangeService.ConfirmAsync(
            requestId,
            request.Token ?? string.Empty,
            cancellationToken);

        if (!confirmed)
            throw new MemberEmailChangeInvalidException();

        ApplicationLogMessages.MemberEmailChanged(
            logger,
            requestId);
    }
}
