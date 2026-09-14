using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Grants no request identity in non-HTTP hosts such as the Worker.</summary>
public class UnauthenticatedTwoFactorCallerProvider : ITwoFactorCallerProvider
{
    /// <inheritdoc/>
    public TwoFactorCaller? GetCurrent()
    {

        return null;
    }
}
