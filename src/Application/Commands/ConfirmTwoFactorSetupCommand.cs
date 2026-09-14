using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Confirms a new authenticator and returns recovery codes once.</summary>
/// <param name="flow">The authorized setup proof.</param>
/// <param name="code">The candidate authenticator code.</param>
public class ConfirmTwoFactorSetupCommand(
    string? flow,
    string? code) : IRequest<TwoFactorRecoveryCodesResponse>
{
    /// <summary>Gets the authorized setup proof.</summary>
    public string? Flow { get; } = flow;

    /// <summary>Gets the candidate authenticator code.</summary>
    public string? Code { get; } = code;
}

/// <summary>Confirms a new authenticator and returns recovery codes once.</summary>
/// <param name="twoFactorService">The transactional second-factor service.</param>
/// <param name="logger">The sanitized operation logger.</param>
public class ConfirmTwoFactorSetupCommandHandler(
    ITwoFactorService twoFactorService,
    ILogger<ConfirmTwoFactorSetupCommandHandler> logger) : IRequestHandler<ConfirmTwoFactorSetupCommand, TwoFactorRecoveryCodesResponse>
{
    /// <summary>Confirms a new authenticator and returns recovery codes once.</summary>
    /// <param name="request">The centrally validated request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The confirmed operation result.</returns>
    public async Task<TwoFactorRecoveryCodesResponse> Handle(
        ConfirmTwoFactorSetupCommand request,
        CancellationToken cancellationToken)
    {
        TwoFactorLogMessages.OperationStarted(logger);
        var result = await twoFactorService.ConfirmSetupAsync(
            request.Flow!,
            request.Code!,
            cancellationToken);
        TwoFactorLogMessages.TwoFactorSetupConfirmed(logger);

        return result;
    }
}
