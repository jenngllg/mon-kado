using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents a password reset email request.
/// </summary>
/// <param name="email">The account email address.</param>
public class RequestPasswordResetCommand(string? email) : IRequest
{
    /// <summary>
    /// Gets the account email address.
    /// </summary>
    public string? Email { get; } = email;
}

/// <summary>
/// Handles password reset email requests.
/// </summary>
/// <param name="passwordResetService">The password reset service.</param>
/// <param name="logger">The logger.</param>
public class RequestPasswordResetCommandHandler(
    IPasswordResetService passwordResetService,
    ILogger<RequestPasswordResetCommandHandler> logger)
    : IRequestHandler<RequestPasswordResetCommand>
{
    /// <summary>
    /// Requests a password reset email without disclosing account eligibility.
    /// </summary>
    /// <param name="request">The password reset email request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task Handle(
        RequestPasswordResetCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.PasswordResetRequestStarted(logger);
        await passwordResetService.RequestAsync(
            request.Email!.Trim(),
            cancellationToken);
        ApplicationLogMessages.PasswordResetRequested(logger);
    }
}
