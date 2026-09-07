using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>Resolves application authentication context from a bound, protected HTTP cookie.</summary>
/// <param name="httpContextAccessor">The current HTTP request accessor.</param>
/// <param name="externalAuthenticationService">The protected Google ticket service.</param>
public class GoogleAuthenticationContextProvider(
    IHttpContextAccessor httpContextAccessor,
    IGoogleExternalAuthenticationService externalAuthenticationService) : IGoogleAuthenticationContextProvider
{
    /// <summary>Reads the protected cookie and verifies its binding before exposing its context.</summary>
    /// <param name="flow">The validated browser-flow binding.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching protected authentication context.</returns>
    /// <exception cref="GoogleAuthenticationFailedException">No valid protected browser ticket exists.</exception>
    /// <exception cref="GoogleFlowBindingMismatchException">The submitted proof belongs to another flow.</exception>
    public async Task<GoogleAuthenticationContext> GetAsync(
        string flow,
        CancellationToken cancellationToken)
    {
        var httpContext = httpContextAccessor.HttpContext ?? throw new GoogleAuthenticationFailedException();
        var ticket = await externalAuthenticationService.AuthenticateAsync(
            httpContext,
            cancellationToken) ?? throw new GoogleAuthenticationFailedException();

        if (!externalAuthenticationService.MatchesFlowBinding(
            ticket.FlowBinding,
            flow))
            throw new GoogleFlowBindingMismatchException();

        return ticket.Context;
    }
}
