using JennGllg.Fr.MonKado.Back.Api.Configurations;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Configuration.Memory;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class LocalUserSecretsConfigurationTests
{
    [Fact]
    public void ConfigureLocalUserSecrets_WhenEnvironmentIsLocal_AddsUserSecretsSource()
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(
                    "LocalUserSecrets:Enabled",
                    "true")
            ]);
        var initialSourceCount = configuration.Sources.Count;
        var environment = new TestWebHostEnvironment("Local");

        // Act
        var result = configuration.ConfigureLocalUserSecrets(environment);

        // Assert
        Assert.Same(
            configuration,
            result);
        Assert.Equal(
            initialSourceCount + 1,
            configuration.Sources.Count);
    }

    [Fact]
    public void ConfigureLocalUserSecrets_WhenHigherPrioritySourceExists_PreservesSourcePriority()
    {
        // Arrange
        var configuration = new ConfigurationManager();
        var higherPrioritySource = new MemoryConfigurationSource
        {
            InitialData =
            [
                new KeyValuePair<string, string?>(
                    "LocalUserSecrets:Enabled",
                    "true")
            ]
        };
        configuration.Sources.Add(higherPrioritySource);

        // Act
        configuration.ConfigureLocalUserSecrets(new TestWebHostEnvironment("Local"));

        // Assert
        Assert.Same(
            higherPrioritySource,
            configuration.Sources[^1]);
    }

    [Fact]
    public void ConfigureLocalUserSecrets_WhenAppSettingsSourceExists_InsertsAfterAppSettings()
    {
        // Arrange
        var configuration = new ConfigurationManager();
        var appSettingsSource = new JsonConfigurationSource
        {
            Optional = true,
            Path = "missing-appsettings.json"
        };
        var higherPrioritySource = new MemoryConfigurationSource
        {
            InitialData =
            [
                new KeyValuePair<string, string?>(
                    "LocalUserSecrets:Enabled",
                    "true")
            ]
        };
        configuration.Sources.Add(appSettingsSource);
        configuration.Sources.Add(higherPrioritySource);

        // Act
        configuration.ConfigureLocalUserSecrets(new TestWebHostEnvironment("Local"));

        // Assert
        var userSecretsSource = Assert.Single(
            configuration.Sources.OfType<JsonConfigurationSource>(),
            source => source != appSettingsSource);
        var appSettingsIndex = configuration.Sources.IndexOf(appSettingsSource);
        var userSecretsIndex = configuration.Sources.IndexOf(userSecretsSource);
        var higherPriorityIndex = configuration.Sources.IndexOf(higherPrioritySource);
        Assert.True(appSettingsIndex < userSecretsIndex);
        Assert.True(userSecretsIndex < higherPriorityIndex);
    }

    [Fact]
    public void ConfigureLocalUserSecrets_WhenLocalLoadingIsDisabled_DoesNotAddUserSecretsSource()
    {
        // Arrange
        var configuration = new ConfigurationManager();
        var initialSourceCount = configuration.Sources.Count;

        // Act
        var result = configuration.ConfigureLocalUserSecrets(new TestWebHostEnvironment("Local"));

        // Assert
        Assert.Same(
            configuration,
            result);
        Assert.Equal(
            initialSourceCount,
            configuration.Sources.Count);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void ConfigureLocalUserSecrets_WhenEnvironmentIsNotLocal_DoesNotAddUserSecretsSource(
        string environmentName)
    {
        // Arrange
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(
                    "LocalUserSecrets:Enabled",
                    "true")
            ]);
        var initialSourceCount = configuration.Sources.Count;
        var environment = new TestWebHostEnvironment(environmentName);

        // Act
        var result = configuration.ConfigureLocalUserSecrets(environment);

        // Assert
        Assert.Same(
            configuration,
            result);
        Assert.Equal(
            initialSourceCount,
            configuration.Sources.Count);
    }
}
