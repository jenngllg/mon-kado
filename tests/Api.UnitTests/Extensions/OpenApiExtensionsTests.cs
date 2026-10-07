using JennGllg.Fr.MonKado.Back.Api.Extensions;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Extensions;

public class OpenApiExtensionsTests
{
    [Fact]
    public void AddApiOpenApi_WhenStaticArtifactIsConfigured_DoesNotRegisterGenerationServices()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenApi:DocumentPath"] = "openapi/v1.json"
            })
            .Build();

        // Act
        var result = services.AddApiOpenApi(configuration);

        // Assert
        Assert.Same(
            services,
            result);
        Assert.Empty(services);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AddApiOpenApi_WhenNoStaticArtifactIsConfigured_RegistersGenerationServices(string? documentPath)
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenApi:DocumentPath"] = documentPath
            })
            .Build();

        // Act
        services.AddApiOpenApi(configuration);

        // Assert
        Assert.NotEmpty(services);
    }

    [Fact]
    public async Task MapApiOpenApi_WhenStaticArtifactIsMissing_FailsWithoutDynamicFallbackAsync()
    {
        // Arrange
        var builder = WebApplication.CreateBuilder();
        builder.Configuration["OpenApi:DocumentPath"] = Path.Combine(
            Path.GetTempPath(),
            Guid.CreateVersion7().ToString(),
            "missing.json");
        await using var app = builder.Build();

        // Act
        var exception = Assert.Throws<FileNotFoundException>(() => app.MapApiOpenApi());

        // Assert
        Assert.Equal(
            "The build-generated OpenAPI document is missing.",
            exception.Message);
    }
}
