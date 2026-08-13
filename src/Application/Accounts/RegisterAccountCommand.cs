using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public sealed record RegisterAccountCommand(string? Email, string? Password, string? DisplayName) : IRequest;
