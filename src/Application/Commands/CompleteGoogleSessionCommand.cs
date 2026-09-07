using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Submits the browser proof to complete a Google sign-in.</summary>
/// <param name="flow">The opaque browser-flow binding.</param>
public class CompleteGoogleSessionCommand(string? flow) : IRequest<AccountSessionTokens>
{
    /// <summary>Gets the browser-flow binding.</summary>
    public string? Flow { get; } = flow;
}

/// <summary>Resolves the browser proof before executing the existing protected Google command.</summary>
/// <param name="contextProvider">The browser authentication context provider.</param>
/// <param name="sender">The validated application request pipeline.</param>
/// <param name="accessTokenService">The minimal MonKado JWT issuer.</param>
public class CompleteGoogleSessionCommandHandler(
    IGoogleAuthenticationContextProvider contextProvider,
    ISender sender,
    IAccessTokenService accessTokenService) : IRequestHandler<CompleteGoogleSessionCommand, AccountSessionTokens>
{
    /// <summary>Completes a sign-in using only server-validated identity and session state.</summary>
    /// <param name="request">The validated browser submission.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The confirmed session tokens.</returns>
    /// <exception cref="GoogleAccountLinkRequiredException">The current password must be proven.</exception>
    /// <exception cref="GoogleAdditionalVerificationRequiredException">Additional identity verification is needed.</exception>
    /// <exception cref="GoogleAuthenticationFailedException">The result does not confirm a complete session.</exception>
    public async Task<AccountSessionTokens> Handle(
        CompleteGoogleSessionCommand request,
        CancellationToken cancellationToken)
    {
        var context = await contextProvider.GetAsync(
            request.Flow!,
            cancellationToken);
        var result = await sender.Send(
            new CompleteGoogleAuthenticationCommand(
                context.Identity,
                context.IsPersistent,
                context.ReturnPath,
                context.FlowId,
                context.ExpectedMemberId,
                context.CurrentSessionId),
            cancellationToken);

        if (result.Outcome == GoogleAuthenticationOutcome.ExplicitLinkRequired)
            throw new GoogleAccountLinkRequiredException();

        if (result.Outcome == GoogleAuthenticationOutcome.AdditionalVerificationRequired)
            throw new GoogleAdditionalVerificationRequiredException();

        if (result.Outcome != GoogleAuthenticationOutcome.SessionCreated ||
            result.Session is null ||
            result.MemberId is not { } memberId ||
            memberId == Guid.Empty)
            throw new GoogleAuthenticationFailedException();

        return new AccountSessionTokens(
            accessTokenService.Create(memberId),
            result.Session.RefreshToken,
            result.Session.RefreshTokenExpiresAt,
            result.Session.IsPersistent);
    }
}
