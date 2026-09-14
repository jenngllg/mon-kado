using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Logging;
using JennGllg.Fr.MonKado.Back.Application.Models;

using MediatR;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Commands;

/// <summary>Retrieves candidate authenticator material for an authorized setup flow.</summary>
/// <param name="flow">The authorized enrollment or replacement proof.</param>
public class CreateTwoFactorSetupCommand(
    string? flow) : IRequest<TwoFactorSetupResponse>
{
    /// <summary>Gets the authorized enrollment or replacement proof.</summary>
    public string? Flow { get; } = flow;
}

/// <summary>Retrieves candidate authenticator material for an authorized setup flow.</summary>
/// <param name="twoFactorService">The transactional second-factor service.</param>
/// <param name="logger">The sanitized operation logger.</param>
public class CreateTwoFactorSetupCommandHandler(
    ITwoFactorService twoFactorService,
    ILogger<CreateTwoFactorSetupCommandHandler> logger) : IRequestHandler<CreateTwoFactorSetupCommand, TwoFactorSetupResponse>
{
    /// <summary>Retrieves candidate authenticator material for an authorized setup flow.</summary>
    /// <param name="request">The centrally validated request.</param>
    /// <param name="cancellationToken">The request cancellation token.</param>
    /// <returns>The confirmed operation result.</returns>
    public async Task<TwoFactorSetupResponse> Handle(
        CreateTwoFactorSetupCommand request,
        CancellationToken cancellationToken)
    {
        TwoFactorLogMessages.OperationStarted(logger);
        var result = await twoFactorService.GetSetupAsync(
            request.Flow!,
            cancellationToken);
        TwoFactorLogMessages.TwoFactorSetupPrepared(logger);

        return result;
    }
}
