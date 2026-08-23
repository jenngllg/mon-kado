using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents an anonymous account password reset.
/// </summary>
/// <param name="userId">The account identifier from the reset link.</param>
/// <param name="token">The password reset token.</param>
/// <param name="newPassword">The new account password.</param>
public class ResetPasswordCommand(
    string? userId,
    string? token,
    string? newPassword) : IRequest, IGenericValidationFailure
{
    /// <summary>
    /// Gets the account identifier from the reset link.
    /// </summary>
    public string? UserId { get; } = userId;

    /// <summary>
    /// Gets the password reset token.
    /// </summary>
    public string? Token { get; } = token;

    /// <summary>
    /// Gets the new account password.
    /// </summary>
    public string? NewPassword { get; } = newPassword;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        var errors = validationErrors.ToArray();

        if (errors.Any(error => error.PropertyName is "userId" or "token"))
            return new PasswordResetInvalidException();

        return new RequestValidationException(errors);
    }
}

/// <summary>
/// Handles anonymous account password resets.
/// </summary>
/// <param name="passwordResetService">The password reset service.</param>
/// <param name="logger">The logger.</param>
public class ResetPasswordCommandHandler(
    IPasswordResetService passwordResetService,
    ILogger<ResetPasswordCommandHandler> logger)
    : IRequestHandler<ResetPasswordCommand>
{
    /// <summary>
    /// Resets the account password and invalidates its security state.
    /// </summary>
    /// <param name="request">The password reset command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    /// <exception cref="PasswordResetInvalidException">The password reset link is invalid or expired.</exception>
    public async Task Handle(
        ResetPasswordCommand request,
        CancellationToken cancellationToken)
    {
        var userId = request.UserId!;
        var token = request.Token!;
        var newPassword = request.NewPassword!;
        var memberId = Guid.Parse(userId);
        ApplicationLogMessages.PasswordResetStarted(
            logger,
            memberId);
        var reset = await passwordResetService.ResetAsync(
            userId,
            token,
            newPassword,
            cancellationToken);

        if (!reset)
            throw new PasswordResetInvalidException();

        ApplicationLogMessages.PasswordResetCompleted(
            logger,
            memberId);
    }
}
