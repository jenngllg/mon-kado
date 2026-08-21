using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Handlers;
/// <summary>
/// Represents login command handler.
/// </summary>
/// <param name="sessionService">The session service.</param>

public class LoginCommandHandler(IAccountSessionService sessionService)
    : IRequestHandler<LoginCommand>
{
    /// <summary>
    /// Executes the handle operation.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var result = await sessionService.LoginAsync(
            request.Email!.Trim(),
            request.Password!,
            request.RememberMe,
            cancellationToken);

        switch (result)
        {
            case AccountLoginResult.Success:

                return;
            case AccountLoginResult.EmailNotConfirmed:

                throw new EmailNotConfirmedException();
            default:

                throw new InvalidCredentialsException();
        }
    }
}
