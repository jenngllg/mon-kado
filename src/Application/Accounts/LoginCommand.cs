using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public sealed record LoginCommand(string? Email, string? Password, bool RememberMe = false) : IRequest;
