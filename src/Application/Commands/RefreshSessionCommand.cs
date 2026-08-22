using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents a request to rotate an authentication session.
/// </summary>
/// <param name="refreshToken">The refresh token.</param>
public class RefreshSessionCommand(string? refreshToken)
    : IRequest<AccountSessionTokens>, IGenericValidationFailure
{
    /// <summary>
    /// Gets the refresh token.
    /// </summary>
    public string? RefreshToken { get; } = refreshToken;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        return new InvalidAuthenticationSessionException();
    }
}

/// <summary>
/// Handles authentication session rotations.
/// </summary>
/// <param name="sessionService">The account session service.</param>
public class RefreshSessionCommandHandler(IAccountSessionService sessionService)
    : IRequestHandler<RefreshSessionCommand, AccountSessionTokens>
{
    /// <summary>
    /// Rotates an authentication session.
    /// </summary>
    /// <param name="request">The refresh command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rotated session tokens.</returns>
    /// <exception cref="InvalidAuthenticationSessionException">The session is invalid.</exception>
    public async Task<AccountSessionTokens> Handle(
        RefreshSessionCommand request,
        CancellationToken cancellationToken)
    {
        var tokens = await sessionService.RefreshAsync(
            request.RefreshToken!,
            cancellationToken);

        return tokens ?? throw new InvalidAuthenticationSessionException();
    }
}
