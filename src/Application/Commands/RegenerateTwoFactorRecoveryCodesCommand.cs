using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Replaces recovery codes after an operation-specific reauthentication.</summary>
/// <param name="flow">The grant bound exclusively to recovery-code regeneration.</param>
public class RegenerateTwoFactorRecoveryCodesCommand(
    string? flow) : IRequest<TwoFactorRecoveryCodesResponse>
{
    /// <summary>Gets the grant bound exclusively to recovery-code regeneration.</summary>
    public string? Flow { get; } = flow;
}

/// <summary>Replaces recovery codes after an operation-specific reauthentication.</summary>
/// <param name="twoFactorService">The transactional second-factor service.</param>
/// <param name="logger">The sanitized operation logger.</param>
public class RegenerateTwoFactorRecoveryCodesCommandHandler(
    ITwoFactorService twoFactorService,
    ILogger<RegenerateTwoFactorRecoveryCodesCommandHandler> logger) : IRequestHandler<RegenerateTwoFactorRecoveryCodesCommand, TwoFactorRecoveryCodesResponse>
{
    /// <summary>Replaces recovery codes after an operation-specific reauthentication.</summary>
    /// <param name="request">The centrally validated request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The confirmed operation result.</returns>
    public async Task<TwoFactorRecoveryCodesResponse> Handle(
        RegenerateTwoFactorRecoveryCodesCommand request,
        CancellationToken cancellationToken)
    {
        TwoFactorLogMessages.OperationStarted(logger);
        var result = await twoFactorService.RegenerateRecoveryCodesAsync(
            request.Flow!,
            cancellationToken);
        TwoFactorLogMessages.TwoFactorRecoveryCodesRegenerated(logger);

        return result;
    }
}
