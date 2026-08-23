using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Options;

public class PasswordResetTokenProviderOptionsTests
{
    [Fact]
    public void Constructor_WhenCalled_ConfiguresDedicatedOneHourProvider()
    {
        // Arrange
        // Act
        var options = new PasswordResetTokenProviderOptions();

        // Assert
        Assert.Equal(
            PasswordResetTokenProviderOptions.ProviderName,
            options.Name);
        Assert.Equal(
            TimeSpan.FromHours(1),
            options.TokenLifespan);
    }
}
