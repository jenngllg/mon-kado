using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>
/// Classifies Google discovery and signing-key retrieval failures as provider unavailability.
/// </summary>
public class GoogleOpenIdConnectConfigurationManager(
    IConfigurationManager<OpenIdConnectConfiguration> innerManager)
    : IConfigurationManager<OpenIdConnectConfiguration>
{
    /// <summary>
    /// Retrieves Google OpenID Connect configuration through the framework cache.
    /// </summary>
    /// <param name="cancel">The cancellation token.</param>
    /// <returns>The current Google OpenID Connect configuration.</returns>
    /// <exception cref="DependencyUnavailableException">Google discovery or key retrieval is unavailable.</exception>
    public async Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        CancellationToken cancel)
    {
        try
        {

            return await innerManager.GetConfigurationAsync(cancel);
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {

            throw;
        }
        catch (Exception exception)
        {

            throw new DependencyUnavailableException(
                "Google OpenID Connect configuration",
                exception);
        }
    }

    /// <summary>
    /// Requests a refresh from the native framework configuration cache.
    /// </summary>
    public void RequestRefresh()
    {
        innerManager.RequestRefresh();
    }
}
