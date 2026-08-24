using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class UnavailableOpenIdConnectConfigurationManager
    : IConfigurationManager<OpenIdConnectConfiguration>
{
    public Task<OpenIdConnectConfiguration> GetConfigurationAsync(
        CancellationToken cancel)
    {
        cancel.ThrowIfCancellationRequested();

        return Task.FromException<OpenIdConnectConfiguration>(
            new HttpRequestException("Google discovery is unavailable."));
    }

    public void RequestRefresh()
    {
    }
}
