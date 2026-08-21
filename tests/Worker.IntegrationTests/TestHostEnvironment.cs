using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace JennGllg.Fr.MonKado.Back.Worker.IntegrationTests;

internal class TestHostEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = "Local";

    public string ApplicationName { get; set; } = "Worker.IntegrationTests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
