using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents request email confirmation command.
/// </summary>
/// <param name="email">The email.</param>
public class RequestEmailConfirmationCommand(string? email) : IRequest
{
    /// <summary>
    /// Gets email.
    /// </summary>
    public string? Email { get; } = email;
}

/// <summary>
/// Handles email confirmation request commands.
/// </summary>
/// <param name="confirmationService">The email confirmation service.</param>
public class RequestEmailConfirmationCommandHandler(IEmailConfirmationService confirmationService)
    : IRequestHandler<RequestEmailConfirmationCommand>
{
    /// <summary>
    /// Requests a new email confirmation message.
    /// </summary>
    /// <param name="request">The confirmation request command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task Handle(
        RequestEmailConfirmationCommand request,
        CancellationToken cancellationToken)
    {
        await confirmationService.RequestAsync(
            request.Email!.Trim(),
            cancellationToken);
    }
}
