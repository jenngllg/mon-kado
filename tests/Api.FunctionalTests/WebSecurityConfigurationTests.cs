namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public sealed class WebSecurityConfigurationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("https://app.example.test/path")]
    [InlineData("https://app.example.test?query=value")]
    [InlineData("https://app.example.test/")]
    [InlineData("http://app.example.test")]
    public void StartupRejectsInvalidLocalOrigins(string origin)
    {
        using SecurityApiFactory factory = new(allowedOrigin: origin);

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("origin", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("*")]
    [InlineData("0.0.0.0")]
    [InlineData("[::]")]
    [InlineData("https://api.example.test")]
    [InlineData("api.example.test:443")]
    public void StartupRejectsInvalidAllowedHosts(string allowedHosts)
    {
        using SecurityApiFactory factory = new(allowedHosts: allowedHosts);

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("host", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionRejectsHttpOrigins()
    {
        using TemporaryKeyDirectory keys = new();
        using SecurityApiFactory factory = new(
            environment: "Production",
            allowedOrigin: "http://localhost:5173",
            allowedHosts: "api.example.test",
            dataProtectionKeysPath: keys.Path);

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("HTTPS", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProductionRequiresDataProtectionKeyPath()
    {
        using SecurityApiFactory factory = new(
            environment: "Production",
            allowedOrigin: "https://app.example.test",
            allowedHosts: "api.example.test");

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("DataProtectionKeysPath", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionRequiresTrustedProxyNetwork()
    {
        using TemporaryKeyDirectory keys = new();
        using SecurityApiFactory factory = new(
            environment: "Production",
            allowedOrigin: "https://app.example.test",
            allowedHosts: "api.example.test",
            dataProtectionKeysPath: keys.Path,
            knownProxyNetwork: null);

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ReverseProxy:KnownNetworks", exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    public void StartupRejectsUnrestrictedOrInvalidProxyNetworks(string knownProxyNetwork)
    {
        using SecurityApiFactory factory = new(knownProxyNetwork: knownProxyNetwork);

        Exception exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("CIDR", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
