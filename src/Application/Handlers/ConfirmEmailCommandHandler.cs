using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Handlers;
/// <summary>
/// Represents confirm email command handler.
/// </summary>
/// <param name="confirmationService">The confirmation service.</param>

public class ConfirmEmailCommandHandler(IEmailConfirmationService confirmationService)
    : IRequestHandler<ConfirmEmailCommand>
{
    /// <summary>
    /// Executes the handle operation.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task Handle(
        ConfirmEmailCommand request,
        CancellationToken cancellationToken)
    {
        var confirmed = await confirmationService.ConfirmAsync(
            request.UserId!,
            request.Token!,
            cancellationToken);

        if (!confirmed)
            throw new EmailConfirmationInvalidException();
    }
}
