using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents completion of a validated Google authentication flow.
/// </summary>
/// <param name="identity">The validated Google identity.</param>
/// <param name="rememberMe">Whether the requested MonKado session is persistent.</param>
/// <param name="returnPath">The validated frontend return path.</param>
/// <param name="flowId">The one-time Google authentication flow identifier.</param>
/// <param name="expectedMemberId">The member resolved before the external cookie was issued, when one existed.</param>
/// <param name="currentSessionId">The optional refresh session proven when the flow started.</param>
public class CompleteGoogleAuthenticationCommand(
    GoogleIdentity? identity,
    bool rememberMe,
    string? returnPath,
    Guid flowId,
    Guid? expectedMemberId,
    Guid? currentSessionId) : IRequest<GoogleAuthenticationResult>, IGenericValidationFailure
{
    /// <summary>
    /// Gets the validated Google identity.
    /// </summary>
    public GoogleIdentity? Identity { get; } = identity;

    /// <summary>
    /// Gets whether the requested MonKado session is persistent.
    /// </summary>
    public bool RememberMe { get; } = rememberMe;

    /// <summary>
    /// Gets the validated frontend return path.
    /// </summary>
    public string? ReturnPath { get; } = returnPath;

    /// <summary>
    /// Gets the one-time Google authentication flow identifier.
    /// </summary>
    public Guid FlowId { get; } = flowId;

    /// <summary>
    /// Gets the member resolved before the external cookie was issued, when one existed.
    /// </summary>
    public Guid? ExpectedMemberId { get; } = expectedMemberId;

    /// <summary>
    /// Gets the optional refresh session proven when the flow started.
    /// </summary>
    public Guid? CurrentSessionId { get; } = currentSessionId;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        _ = validationErrors;

        return new GoogleAuthenticationFailedException();
    }
}

/// <summary>
/// Handles completion of validated Google authentication flows.
/// </summary>
/// <param name="googleAccountSessionService">The Google account session service.</param>
public class CompleteGoogleAuthenticationCommandHandler(
    IGoogleAccountSessionService googleAccountSessionService)
    : IRequestHandler<CompleteGoogleAuthenticationCommand, GoogleAuthenticationResult>
{
    /// <summary>
    /// Completes a validated Google authentication flow.
    /// </summary>
    /// <param name="request">The completion command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The completion outcome.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    /// <exception cref="GoogleAuthenticationFailedException">The protected Google identity cannot be completed safely.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public Task<GoogleAuthenticationResult> Handle(
        CompleteGoogleAuthenticationCommand request,
        CancellationToken cancellationToken)
    {
        var context = new GoogleAuthenticationContext(
            request.Identity!,
            request.RememberMe,
            request.ReturnPath!,
            request.FlowId,
            request.ExpectedMemberId,
            request.CurrentSessionId);

        return googleAccountSessionService.CompleteAsync(
            context,
            cancellationToken);
    }
}
