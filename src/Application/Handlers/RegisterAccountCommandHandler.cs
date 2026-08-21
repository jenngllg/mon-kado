using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Handlers;
/// <summary>
/// Represents register account command handler.
/// </summary>
/// <param name="registrationService">The registration service.</param>

public class RegisterAccountCommandHandler(IAccountRegistrationService registrationService)
    : IRequestHandler<RegisterAccountCommand>
{
    /// <summary>
    /// Executes the handle operation.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task Handle(
        RegisterAccountCommand request,
        CancellationToken cancellationToken)
    {
        await registrationService.RegisterAsync(
            request.Email!.Trim(),
            request.Password!,
            request.DisplayName!.Trim(),
            cancellationToken);
    }
}
