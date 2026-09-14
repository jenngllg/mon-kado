using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Completes a verified sign-in or advances a recovery flow.</summary>
/// <param name="flow">The opaque sign-in proof.</param>
/// <param name="code">The optional authenticator code.</param>
/// <param name="recoveryCode">The optional recovery code.</param>
public class CompleteTwoFactorCommand(
    string? flow,
    string? code,
    string? recoveryCode) : IRequest<TwoFactorCompletionResult>
{
    /// <summary>Gets the opaque sign-in proof.</summary>
    public string? Flow { get; } = flow;

    /// <summary>Gets the optional authenticator code.</summary>
    public string? Code { get; } = code;

    /// <summary>Gets the optional recovery code.</summary>
    public string? RecoveryCode { get; } = recoveryCode;
}

/// <summary>Completes a verified sign-in or advances a recovery flow.</summary>
/// <param name="twoFactorService">The transactional second-factor service.</param>
/// <param name="logger">The sanitized operation logger.</param>
public class CompleteTwoFactorCommandHandler(
    ITwoFactorService twoFactorService,
    ILogger<CompleteTwoFactorCommandHandler> logger) : IRequestHandler<CompleteTwoFactorCommand, TwoFactorCompletionResult>
{
    /// <summary>Completes a verified sign-in or advances a recovery flow.</summary>
    /// <param name="request">The centrally validated request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The confirmed operation result.</returns>
    public async Task<TwoFactorCompletionResult> Handle(
        CompleteTwoFactorCommand request,
        CancellationToken cancellationToken)
    {
        TwoFactorLogMessages.OperationStarted(logger);
        var result = await twoFactorService.CompleteAsync(
            request.Flow!,
            request.Code,
            request.RecoveryCode,
            cancellationToken);
        TwoFactorLogMessages.TwoFactorCompletionProcessed(logger);

        return result;
    }
}
