using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents a current member password update.
/// </summary>
/// <param name="memberId">The authenticated member identifier.</param>
/// <param name="currentPassword">The current member password.</param>
/// <param name="newPassword">The new member password.</param>
public class UpdateMemberPasswordCommand(
    Guid memberId,
    string? currentPassword,
    string? newPassword) : IRequest, IGenericValidationFailure
{
    /// <summary>
    /// Gets the authenticated member identifier.
    /// </summary>
    public Guid MemberId { get; } = memberId;

    /// <summary>
    /// Gets the current member password.
    /// </summary>
    public string? CurrentPassword { get; } = currentPassword;

    /// <summary>
    /// Gets the new member password.
    /// </summary>
    public string? NewPassword { get; } = newPassword;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {

        if (MemberId == Guid.Empty)
            return new InvalidAuthenticationSessionException();

        return new RequestValidationException(validationErrors);
    }
}

/// <summary>
/// Handles current member password updates.
/// </summary>
/// <param name="memberPasswordService">The member password service.</param>
/// <param name="logger">The logger.</param>
public class UpdateMemberPasswordCommandHandler(
    IMemberPasswordService memberPasswordService,
    ILogger<UpdateMemberPasswordCommandHandler> logger)
    : IRequestHandler<UpdateMemberPasswordCommand>
{
    /// <summary>
    /// Changes the current member password.
    /// </summary>
    /// <param name="request">The password update command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task Handle(
        UpdateMemberPasswordCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.MemberPasswordChangeStarted(
            logger,
            request.MemberId);
        var memberExists = await memberPasswordService.ChangeAsync(
            request.MemberId,
            request.CurrentPassword!,
            request.NewPassword!,
            cancellationToken);

        if (!memberExists)
            throw new InvalidAuthenticationSessionException();

        ApplicationLogMessages.MemberPasswordChanged(
            logger,
            request.MemberId);
    }
}
