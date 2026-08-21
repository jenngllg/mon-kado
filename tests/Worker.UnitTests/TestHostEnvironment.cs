using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class TestHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "Worker.UnitTests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
