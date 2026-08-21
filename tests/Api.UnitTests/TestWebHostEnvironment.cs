using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class TestWebHostEnvironment(string environmentName) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Api.UnitTests";

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public string EnvironmentName { get; set; } = environmentName;

    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();

    public string WebRootPath { get; set; } = AppContext.BaseDirectory;
}
