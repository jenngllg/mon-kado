using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Options;

public class GuestSessionOptionsValidatorTests
{
    private readonly GuestSessionOptionsValidator _validator = new();

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    [InlineData(180, true)]
    [InlineData(181, false)]
    public void Validate_WhenLifetimeVaries_ReturnsExpectedResult(
        int lifetimeDays,
        bool expectedSuccess)
    {
        // Arrange
        var options = new GuestSessionOptions
        {
            Lifetime = TimeSpan.FromDays(lifetimeDays)
        };

        // Act
        var result = _validator.Validate(
            null,
            options);

        // Assert
        Assert.Equal(
            expectedSuccess,
            result.Succeeded);
    }

    [Fact]
    public void Constructor_WhenNoConfigurationIsProvided_Uses180Days()
    {
        // Arrange
        // Act
        var options = new GuestSessionOptions();

        // Assert
        Assert.Equal(
            TimeSpan.FromDays(180),
            options.Lifetime);
    }
}
