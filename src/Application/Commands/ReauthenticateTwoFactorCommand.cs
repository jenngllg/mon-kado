using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Verifies the current factor and binds a management grant to one operation.</summary>
/// <param name="memberId">The authenticated member identifier.</param>
/// <param name="accessTokenId">The registered token identifying the authenticated session.</param>
/// <param name="purpose">The management operation requested by the client.</param>
/// <param name="code">The current authenticator code.</param>
/// <param name="recoveryCode">The recovery code accepted only for forced replacement.</param>
public class ReauthenticateTwoFactorCommand(
    Guid memberId,
    Guid accessTokenId,
    TwoFactorFlowPurpose? purpose,
    string? code,
    string? recoveryCode) : IRequest<TwoFactorChallengeResponse>
{
    /// <summary>Gets the authenticated member identifier.</summary>
    public Guid MemberId { get; } = memberId;

    /// <summary>Gets the registered token identifying the authenticated session.</summary>
    public Guid AccessTokenId { get; } = accessTokenId;

    /// <summary>Gets the management operation requested by the client.</summary>
    public TwoFactorFlowPurpose? Purpose { get; } = purpose;

    /// <summary>Gets the current authenticator code.</summary>
    public string? Code { get; } = code;

    /// <summary>Gets the recovery code accepted only for forced replacement.</summary>
    public string? RecoveryCode { get; } = recoveryCode;
}

/// <summary>Verifies the current factor and binds a management grant to one operation.</summary>
/// <param name="twoFactorService">The transactional second-factor service.</param>
/// <param name="logger">The sanitized operation logger.</param>
public class ReauthenticateTwoFactorCommandHandler(
    ITwoFactorService twoFactorService,
    ILogger<ReauthenticateTwoFactorCommandHandler> logger) : IRequestHandler<ReauthenticateTwoFactorCommand, TwoFactorChallengeResponse>
{
    /// <summary>Verifies the current factor and binds a management grant to one operation.</summary>
    /// <param name="request">The centrally validated request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The confirmed operation result.</returns>
    public async Task<TwoFactorChallengeResponse> Handle(
        ReauthenticateTwoFactorCommand request,
        CancellationToken cancellationToken)
    {
        TwoFactorLogMessages.OperationStarted(logger);
        var result = await twoFactorService.ReauthenticateAsync(
            request.MemberId,
            request.AccessTokenId,
            request.Purpose!.Value,
            request.Code,
            request.RecoveryCode,
            cancellationToken);
        TwoFactorLogMessages.TwoFactorManagementAuthorized(logger);

        return result;
    }
}
