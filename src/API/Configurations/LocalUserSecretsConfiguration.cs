using JennGllg.Fr.MonKado.Back.Api.Options;

using Microsoft.Extensions.Configuration.Json;

namespace JennGllg.Fr.MonKado.Back.Api.Configurations;

/// <summary>
/// Configures local development secrets.
/// </summary>
public static class LocalUserSecretsConfiguration
{
    private const string EnabledSetting = "LocalUserSecrets:Enabled";

    /// <summary>
    /// Adds the API user-secrets provider when the application runs in the Local environment.
    /// </summary>
    /// <param name="configuration">The application configuration manager.</param>
    /// <param name="environment">The host environment.</param>
    /// <returns>The configuration manager.</returns>
    public static ConfigurationManager ConfigureLocalUserSecrets(
        this ConfigurationManager configuration,
        IHostEnvironment environment)
    {
        if (!environment.IsEnvironment("Local"))
            return configuration;

        if (!configuration.GetValue<bool>(EnabledSetting))
            return configuration;

        var initialSourceCount = configuration.Sources.Count;
        configuration.AddUserSecrets<GoogleAuthenticationOptions>(optional: true);
        var userSecretSources = configuration.Sources
            .Skip(initialSourceCount)
            .ToArray();

        foreach (var source in userSecretSources)
            configuration.Sources.Remove(source);

        var insertionIndex = configuration.Sources
            .Select((source, index) => (source, index))
            .Where(item => item.source is JsonConfigurationSource)
            .Select(item => item.index + 1)
            .DefaultIfEmpty()
            .Max();

        foreach (var source in userSecretSources)
        {
            configuration.Sources.Insert(
                insertionIndex,
                source);
            insertionIndex++;
        }

        return configuration;
    }
}
