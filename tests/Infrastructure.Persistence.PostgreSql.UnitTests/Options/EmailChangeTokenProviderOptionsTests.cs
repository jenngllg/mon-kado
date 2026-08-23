using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Options;

public class EmailChangeTokenProviderOptionsTests
{
    [Fact]
    public void Constructor_WhenCalled_ConfiguresDedicatedTwentyFourHourProvider()
    {
        // Arrange
        // Act
        var options = new EmailChangeTokenProviderOptions();

        // Assert
        Assert.Equal(
            EmailChangeTokenProviderOptions.ProviderName,
            options.Name);
        Assert.Equal(
            TimeSpan.FromHours(24),
            options.TokenLifespan);
    }
}
