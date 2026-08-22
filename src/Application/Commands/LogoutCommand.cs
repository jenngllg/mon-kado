using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents a request to end the current browser session.
/// </summary>
/// <param name="refreshToken">The refresh token currently held by the browser.</param>
public class LogoutCommand(string? refreshToken) : IRequest
{
    /// <summary>
    /// Gets the refresh token currently held by the browser.
    /// </summary>
    public string? RefreshToken { get; } = refreshToken;
}

/// <summary>
/// Handles current browser session logout commands.
/// </summary>
/// <param name="sessionService">The account session service.</param>
/// <param name="logger">The logger.</param>
public class LogoutCommandHandler(
    IAccountSessionService sessionService,
    ILogger<LogoutCommandHandler> logger) : IRequestHandler<LogoutCommand>
{
    /// <summary>
    /// Ends the current browser session.
    /// </summary>
    /// <param name="request">The logout command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task Handle(
        LogoutCommand request,
        CancellationToken cancellationToken)
    {
        ApplicationLogMessages.CurrentSessionLogoutStarted(logger);
        await sessionService.LogoutAsync(
            request.RefreshToken,
            cancellationToken);
        ApplicationLogMessages.CurrentSessionLogoutCompleted(logger);
    }
}
