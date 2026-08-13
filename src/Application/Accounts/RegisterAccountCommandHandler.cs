using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public sealed class RegisterAccountCommandHandler(IAccountRegistrationService registrationService)
    : IRequestHandler<RegisterAccountCommand>
{
    public async Task Handle(RegisterAccountCommand request, CancellationToken cancellationToken)
    {
        await registrationService.RegisterAsync(
            request.Email!.Trim(),
            request.Password!,
            request.DisplayName!.Trim(),
            cancellationToken);
    }
}
