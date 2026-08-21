using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests;

public class MaximumPasswordLengthValidatorTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("password", false)]
    public void IsTooLong_WhenPasswordDoesNotExceedLimit_ReturnsFalse(
        string? password,
        bool expected)
    {
        // Arrange
        // Act
        var result = MaximumPasswordLengthValidator<MonKadoUser>.IsTooLong(password);

        // Assert
        Assert.Equal(
            expected,
            result);
    }

    [Fact]
    public void IsTooLong_WhenPasswordExceedsLimit_ReturnsTrue()
    {
        // Arrange
        var password = new string(
            'a',
            129);

        // Act
        var result = MaximumPasswordLengthValidator<MonKadoUser>.IsTooLong(password);

        // Assert
        Assert.True(result);
    }
}
