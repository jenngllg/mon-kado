using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents login command.
/// </summary>
/// <param name="email">The email.</param>
/// <param name="password">The password.</param>
/// <param name="rememberMe">The remember me.</param>
/// <param name="currentRefreshToken">The refresh token currently held by the browser.</param>
public class LoginCommand(
    string? email,
    string? password,
    bool rememberMe = false,
    string? currentRefreshToken = null) : IRequest<AccountSessionTokens>
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
    /// Gets remember me.
    /// </summary>
    public bool RememberMe { get; } = rememberMe;

    /// <summary>
    /// Gets the refresh token currently held by the browser.
    /// </summary>
    public string? CurrentRefreshToken { get; } = currentRefreshToken;
}

/// <summary>
/// Handles account login commands.
/// </summary>
/// <param name="sessionService">The account session service.</param>
public class LoginCommandHandler(IAccountSessionService sessionService)
    : IRequestHandler<LoginCommand, AccountSessionTokens>
{
    /// <summary>
    /// Authenticates an account and creates its session tokens.
    /// </summary>
    /// <param name="request">The login command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created session tokens.</returns>
    /// <exception cref="EmailNotConfirmedException">The email address is not confirmed.</exception>
    /// <exception cref="InvalidCredentialsException">The credentials are invalid.</exception>
    public async Task<AccountSessionTokens> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        var result = await sessionService.LoginAsync(
            request.Email!.Trim(),
            request.Password!,
            request.RememberMe,
            request.CurrentRefreshToken,
            cancellationToken);

        if (result.Result == AccountLoginResult.EmailNotConfirmed)
            throw new EmailNotConfirmedException();

        if (result.Result != AccountLoginResult.Success)
            throw new InvalidCredentialsException();

        return result.Tokens ?? throw new InvalidOperationException(
            "A successful login must return session tokens.");
    }
}
