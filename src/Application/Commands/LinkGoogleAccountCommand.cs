using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>
/// Represents an explicit Google account link using the current MonKado password.
/// </summary>
/// <param name="identity">The validated Google identity.</param>
/// <param name="rememberMe">Whether the requested MonKado session is persistent.</param>
/// <param name="returnPath">The validated frontend return path.</param>
/// <param name="flowId">The one-time Google authentication flow identifier.</param>
/// <param name="expectedMemberId">The member resolved before the external cookie was issued, when one existed.</param>
/// <param name="currentSessionId">The optional refresh session proven when the flow started.</param>
/// <param name="currentPassword">The exact current MonKado password.</param>
public class LinkGoogleAccountCommand(
    GoogleIdentity? identity,
    bool rememberMe,
    string? returnPath,
    Guid flowId,
    Guid? expectedMemberId,
    Guid? currentSessionId,
    string? currentPassword) : IRequest<AccountSessionTokens>, IGenericValidationFailure
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

    /// <summary>
    /// Gets the exact current MonKado password.
    /// </summary>
    public string? CurrentPassword { get; } = currentPassword;

    /// <inheritdoc />
    Exception IGenericValidationFailure.CreateValidationException(
        IEnumerable<ValidationError> validationErrors)
    {
        var errors = validationErrors.ToArray();
        var onlyPasswordFailed = errors.All(error =>
            string.Equals(
                error.PropertyName,
                "currentPassword",
                StringComparison.Ordinal));

        if (onlyPasswordFailed)
            return new RequestValidationException(errors);

        return new GoogleAuthenticationFailedException();
    }
}

/// <summary>
/// Handles explicit Google account links.
/// </summary>
/// <param name="googleAccountSessionService">The Google account session service.</param>
public class LinkGoogleAccountCommandHandler(
    IGoogleAccountSessionService googleAccountSessionService)
    : IRequestHandler<LinkGoogleAccountCommand, AccountSessionTokens>
{
    /// <summary>
    /// Verifies the current password, links Google and creates MonKado session tokens.
    /// </summary>
    /// <param name="request">The explicit link command.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created MonKado session tokens.</returns>
    /// <exception cref="DependencyUnavailableException">PostgreSQL is unavailable.</exception>
    /// <exception cref="GoogleAuthenticationFailedException">The protected Google identity cannot be linked safely.</exception>
    /// <exception cref="GoogleAccountLinkFailedException">The current account could not be proven.</exception>
    /// <exception cref="GoogleAccountLinkConflictException">The Google login conflicts with current account state.</exception>
    /// <exception cref="InvalidOperationException">The service reports success without session tokens.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public async Task<AccountSessionTokens> Handle(
        LinkGoogleAccountCommand request,
        CancellationToken cancellationToken)
    {
        var context = new GoogleAuthenticationContext(
            request.Identity!,
            request.RememberMe,
            request.ReturnPath!,
            request.FlowId,
            request.ExpectedMemberId,
            request.CurrentSessionId);
        var result = await googleAccountSessionService.LinkAsync(
            context,
            request.CurrentPassword!,
            cancellationToken);

        if (result.Outcome == GoogleAccountLinkOutcome.InvalidCredentials)
            throw new GoogleAccountLinkFailedException();

        if (result.Outcome == GoogleAccountLinkOutcome.Conflict)
            throw new GoogleAccountLinkConflictException();

        return result.Tokens ?? throw new InvalidOperationException(
            "A successful Google account link must return session tokens.");
    }
}
