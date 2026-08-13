using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public sealed class RequestEmailConfirmationCommandHandler(IEmailConfirmationService confirmationService)
    : IRequestHandler<RequestEmailConfirmationCommand>
{
    public async Task Handle(RequestEmailConfirmationCommand request, CancellationToken cancellationToken)
    {
        await confirmationService.RequestAsync(
            request.Email!.Trim(),
            cancellationToken);
    }
}
