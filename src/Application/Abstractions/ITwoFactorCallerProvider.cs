using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Reads the validated Bearer identity without trusting request-body identifiers.</summary>
public interface ITwoFactorCallerProvider
{
    /// <summary>Gets the current authenticated caller, or null for anonymous sign-in continuations.</summary>
    /// <returns>The validated caller identifiers when a Bearer principal exists.</returns>
    TwoFactorCaller? GetCurrent();
}
