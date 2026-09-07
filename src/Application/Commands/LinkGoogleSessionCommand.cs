using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Submits browser and password proofs for an explicit Google account link.</summary>
/// <param name="flow">The opaque browser-flow binding.</param>
/// <param name="currentPassword">The exact current MonKado password.</param>
public class LinkGoogleSessionCommand(
    string? flow,
    string? currentPassword) : IRequest<AccountSessionTokens>
{
    /// <summary>Gets the browser-flow binding.</summary>
    public string? Flow { get; } = flow;
    /// <summary>Gets the exact current MonKado password.</summary>
    public string? CurrentPassword { get; } = currentPassword;
}

/// <summary>Resolves the browser proof before executing the existing protected link command.</summary>
/// <param name="contextProvider">The browser authentication context provider.</param>
/// <param name="sender">The validated application request pipeline.</param>
public class LinkGoogleSessionCommandHandler(
    IGoogleAuthenticationContextProvider contextProvider,
    ISender sender) : IRequestHandler<LinkGoogleSessionCommand, AccountSessionTokens>
{
    /// <summary>Links a server-validated identity after proving the current MonKado password.</summary>
    /// <param name="request">The validated browser submission.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created MonKado session tokens.</returns>
    /// <exception cref="GoogleAccountLinkFailedException">The browser proof belongs to another flow.</exception>
    public async Task<AccountSessionTokens> Handle(
        LinkGoogleSessionCommand request,
        CancellationToken cancellationToken)
    {
        GoogleAuthenticationContext context;

        try
        {
            context = await contextProvider.GetAsync(
                request.Flow!,
                cancellationToken);
        }
        catch (GoogleFlowBindingMismatchException)
        {

            throw new GoogleAccountLinkFailedException();
        }

        return await sender.Send(
            new LinkGoogleAccountCommand(
                context.Identity,
                context.IsPersistent,
                context.ReturnPath,
                context.FlowId,
                context.ExpectedMemberId,
                context.CurrentSessionId,
                request.CurrentPassword),
            cancellationToken);
    }
}
