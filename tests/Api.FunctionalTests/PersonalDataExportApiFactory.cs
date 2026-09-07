using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class PersonalDataExportApiFactory(IPersonalDataExportService service) : RegistrationApiFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPersonalDataExportService>();
                services.AddSingleton(service);
            });
    }
}
