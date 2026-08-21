namespace JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap.UnitTests;

public class ProgramTests
{
    [Fact]
    public async Task Main_WhenCredentialsAreMissing_ReturnsConfigurationError()
    {
        // Arrange
        var originalClientId = Environment.GetEnvironmentVariable(
            GmailOAuthBootstrapApplication.ClientIdVariable);
        var originalClientSecret = Environment.GetEnvironmentVariable(
            GmailOAuthBootstrapApplication.ClientSecretVariable);
        Environment.SetEnvironmentVariable(
            GmailOAuthBootstrapApplication.ClientIdVariable,
            null);
        Environment.SetEnvironmentVariable(
            GmailOAuthBootstrapApplication.ClientSecretVariable,
            null);

        try
        {
            // Act
            var result = await Program.Main([]);

            // Assert
            Assert.Equal(
                1,
                result);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                GmailOAuthBootstrapApplication.ClientIdVariable,
                originalClientId);
            Environment.SetEnvironmentVariable(
                GmailOAuthBootstrapApplication.ClientSecretVariable,
                originalClientSecret);
        }
    }
}
