using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public sealed class LoginCommandHandler(IAccountSessionService sessionService)
    : IRequestHandler<LoginCommand>
{
    public async Task Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        AccountLoginResult result = await sessionService.LoginAsync(
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
