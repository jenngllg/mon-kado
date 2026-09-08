using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using Microsoft.Extensions.Logging;

namespace JennGllg.Fr.MonKado.Back.Application.Logging;

/// <summary>Logs administrative revocations without credentials or support references.</summary>
public static partial class AdministrativeSessionRevocationLogMessages
{
    /// <summary>Records a committed revocation.</summary>
    /// <param name="logger">The correlated logger.</param>
    /// <param name="administratorId">The actor identifier.</param>
    /// <param name="memberId">The target identifier.</param>
    /// <param name="operationId">The audit identifier.</param>
    [LoggerMessage(
        LogEventIds.AdministrativeSessionsRevoked,
        LogLevel.Information,
        "Administrator {AdministratorId} revoked sessions of member {MemberId} in operation {OperationId}")]
    public static partial void Executed(
        ILogger logger,
        Guid administratorId,
        Guid memberId,
        Guid operationId);
}
