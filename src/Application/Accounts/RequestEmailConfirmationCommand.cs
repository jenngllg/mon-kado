using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public sealed record RequestEmailConfirmationCommand(string? Email) : IRequest;
