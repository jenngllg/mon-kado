using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents register account command.
/// </summary>
/// <param name="email">The email.</param>
/// <param name="password">The password.</param>
/// <param name="displayName">The display name.</param>
public class RegisterAccountCommand(
    string? email,
    string? password,
    string? displayName) : IRequest
{
    /// <summary>
    /// Gets email.
    /// </summary>
    public string? Email { get; } = email;

    /// <summary>
    /// Gets password.
    /// </summary>
    public string? Password { get; } = password;

    /// <summary>
    /// Gets display name.
    /// </summary>
    public string? DisplayName { get; } = displayName;
}

/// <summary>
/// Handles account registration commands.
/// </summary>
/// <param name="registrationService">The account registration service.</param>
public class RegisterAccountCommandHandler(IAccountRegistrationService registrationService)
    : IRequestHandler<RegisterAccountCommand>
{
    /// <summary>
    /// Registers an account.
    /// </summary>
    /// <param name="request">The registration command.</param>
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
