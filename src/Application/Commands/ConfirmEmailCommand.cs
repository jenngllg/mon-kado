using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents confirm email command.
/// </summary>
/// <param name="userId">The user id.</param>
/// <param name="token">The token.</param>
public class ConfirmEmailCommand(
    string? userId,
    string? token)
    : IRequest, IGenericValidationFailure
{
    /// <summary>
    /// Gets user id.
    /// </summary>
    public string? UserId { get; } = userId;

    /// <summary>
    /// Gets token.
    /// </summary>
    public string? Token { get; } = token;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        return new EmailConfirmationInvalidException();
    }
}

/// <summary>
/// Handles email confirmation commands.
/// </summary>
/// <param name="confirmationService">The email confirmation service.</param>
/// <param name="logger">The logger.</param>
public class ConfirmEmailCommandHandler(
    IEmailConfirmationService confirmationService,
    ILogger<ConfirmEmailCommandHandler> logger)
    : IRequestHandler<ConfirmEmailCommand>
{
    /// <summary>
    /// Confirms an email address.
    /// </summary>
    /// <param name="request">The confirmation command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="EmailConfirmationInvalidException">The confirmation is invalid.</exception>
    public async Task Handle(
        ConfirmEmailCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.EmailConfirmationStarted(logger);
        var confirmed = await confirmationService.ConfirmAsync(
            request.UserId!,
            request.Token!,
            cancellationToken);

        if (!confirmed)
            throw new EmailConfirmationInvalidException();

        ApplicationLogMessages.EmailConfirmationCompleted(logger);
    }
}
