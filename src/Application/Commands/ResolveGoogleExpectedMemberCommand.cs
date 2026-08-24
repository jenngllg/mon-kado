using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents resolution of the member expected by a validated Google callback.
/// </summary>
/// <param name="identity">The validated Google identity.</param>
public class ResolveGoogleExpectedMemberCommand(GoogleIdentity? identity)
    : IRequest<Guid?>, IGenericValidationFailure
{
    /// <summary>
    /// Gets the validated Google identity.
    /// </summary>
    public GoogleIdentity? Identity { get; } = identity;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        return new GoogleAuthenticationFailedException();
    }
}

/// <summary>
/// Handles resolution of the member expected by validated Google callbacks.
/// </summary>
/// <param name="googleAccountSessionService">The Google account session service.</param>
public class ResolveGoogleExpectedMemberCommandHandler(
    IGoogleAccountSessionService googleAccountSessionService)
    : IRequestHandler<ResolveGoogleExpectedMemberCommand, Guid?>
{
    /// <summary>
    /// Resolves the member linked by subject first and normalized email second.
    /// </summary>
    /// <param name="request">The expected-member command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The expected member identifier, or <see langword="null" />.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    /// <exception cref="GoogleAuthenticationFailedException">The protected Google identity is invalid.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public Task<Guid?> Handle(
        ResolveGoogleExpectedMemberCommand request,
        CancellationToken cancellationToken)
    {

        return googleAccountSessionService.ResolveExpectedMemberIdAsync(
            request.Identity!,
            cancellationToken);
    }
}
