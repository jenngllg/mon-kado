using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class AdministrativeAuditApiFactory(
    IAdministrativeAuditService service,
    IAdministratorAccessService accessService) : RegistrationApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAdministrativeAuditService>();
            services.RemoveAll<IAdministratorAccessService>();
            services.AddSingleton(service);
            services.AddSingleton(accessService);
        });
    }
}
