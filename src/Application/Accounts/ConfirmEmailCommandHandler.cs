using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public sealed class ConfirmEmailCommandHandler(IEmailConfirmationService confirmationService)
    : IRequestHandler<ConfirmEmailCommand>
{
    public async Task Handle(ConfirmEmailCommand request, CancellationToken cancellationToken)
    {
        bool confirmed = await confirmationService.ConfirmAsync(
            request.UserId!,
            request.Token!,
            cancellationToken);

        if (!confirmed)
        {
            throw new EmailConfirmationInvalidException();
        }
    }
}
