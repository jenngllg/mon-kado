using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents a request to change the current member email address.
/// </summary>
/// <param name="memberId">The authenticated member identifier.</param>
/// <param name="email">The requested email address.</param>
/// <param name="currentPassword">The current member password.</param>
/// <param name="expectedVersion">The member version supplied by the client.</param>
public class RequestMemberEmailChangeCommand(
    Guid memberId,
    string? email,
    string? currentPassword,
    uint expectedVersion) : IRequest, IGenericValidationFailure
{
    /// <summary>
    /// Gets the authenticated member identifier.
    /// </summary>
    public Guid MemberId { get; } = memberId;

    /// <summary>
    /// Gets the requested email address.
    /// </summary>
    public string? Email { get; } = email;

    /// <summary>
    /// Gets the current member password.
    /// </summary>
    public string? CurrentPassword { get; } = currentPassword;

    /// <summary>
    /// Gets the member version supplied by the client.
    /// </summary>
    public uint ExpectedVersion { get; } = expectedVersion;

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
/// Handles member email change requests.
/// </summary>
/// <param name="memberEmailChangeService">The member email change service.</param>
/// <param name="logger">The logger.</param>
public class RequestMemberEmailChangeCommandHandler(
    IMemberEmailChangeService memberEmailChangeService,
    ILogger<RequestMemberEmailChangeCommandHandler> logger)
    : IRequestHandler<RequestMemberEmailChangeCommand>
{
    /// <summary>
    /// Requests a change to the current member email address.
    /// </summary>
    /// <param name="request">The email change command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task Handle(
        RequestMemberEmailChangeCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.MemberEmailChangeRequestStarted(
            logger,
            request.MemberId);
        var memberExists = await memberEmailChangeService.RequestAsync(
            request.MemberId,
            request.Email?.Trim() ?? string.Empty,
            request.CurrentPassword ?? string.Empty,
            request.ExpectedVersion,
            cancellationToken);

        if (!memberExists)
            throw new InvalidAuthenticationSessionException();

        ApplicationLogMessages.MemberEmailChangeRequested(
            logger,
            request.MemberId);
    }
}
