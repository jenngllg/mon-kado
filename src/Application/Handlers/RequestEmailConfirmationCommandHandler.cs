using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Handlers;
/// <summary>
/// Represents request email confirmation command handler.
/// </summary>
/// <param name="confirmationService">The confirmation service.</param>

public class RequestEmailConfirmationCommandHandler(IEmailConfirmationService confirmationService)
    : IRequestHandler<RequestEmailConfirmationCommand>
{
    /// <summary>
    /// Executes the handle operation.
    /// </summary>
    /// <param name="request">The request.</param>
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
