namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class WebSecurityConfigurationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("https://app.example.test/path")]
    [InlineData("https://app.example.test?query=value")]
    [InlineData("https://app.example.test/")]
    [InlineData("http://app.example.test")]
    public void Configure_WhenStartup_RejectsInvalidLocalOrigins(string origin)
    {
        // Arrange
        using var factory = new SecurityApiFactory(allowedOrigin: origin);

        // Act
        var exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

        // Assert
        Assert.Contains(
            "origin",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("[::]")]
    [InlineData("https://api.example.test")]
    [InlineData("api.example.test:443")]
    public void Configure_WhenStartup_RejectsInvalidAllowedHosts(string allowedHosts)
    {
        // Arrange
        using var factory = new SecurityApiFactory(allowedHosts: allowedHosts);

        // Act
        var exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

        // Assert
        Assert.Contains(
            "host",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Configure_WhenProduction_RejectsHttpOrigins()
    {
        // Arrange
        using var keys = new TemporaryKeyDirectory();
        using var factory = new SecurityApiFactory(
            environment: "Production",
            allowedOrigin: "http://localhost:5173",
            allowedHosts: "api.example.test",
            dataProtectionKeysPath: keys.Path);

        // Act
        var exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

        // Assert
        Assert.Contains(
            "HTTPS",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Configure_WhenProduction_RequiresDataProtectionKeyPath()
    {
        // Arrange
        using var factory = new SecurityApiFactory(
            environment: "Production",
            allowedOrigin: "https://app.example.test",
            allowedHosts: "api.example.test");

        // Act
        var exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

        // Assert
        Assert.Contains(
            "DataProtection:KeysPath",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Configure_WhenProduction_RequiresTrustedProxyNetwork()
    {
        // Arrange
        using var keys = new TemporaryKeyDirectory();
        using var factory = new SecurityApiFactory(
            environment: "Production",
            allowedOrigin: "https://app.example.test",
            allowedHosts: "api.example.test",
            dataProtectionKeysPath: keys.Path,
            knownProxyNetwork: null);

        // Act
        var exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

        // Assert
        Assert.Contains(
            "ReverseProxy:KnownNetworks",
            exception.ToString(),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void Configure_WhenStartup_RejectsUnrestrictedOrInvalidProxyNetworks(string knownProxyNetwork)
    {
        // Arrange
        using var factory = new SecurityApiFactory(knownProxyNetwork: knownProxyNetwork);

        // Act
        var exception = Assert.ThrowsAny<Exception>(factory.CreateClient);

        // Assert
        Assert.Contains(
            "CIDR",
            exception.ToString(),
            StringComparison.OrdinalIgnoreCase);
    }
}
