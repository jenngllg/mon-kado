using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class EmailAddressValidationTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("lea@example.fr", true)]
    public void IsWithinMaximumLength_WhenEmailIsProvided_ReturnsExpectedResult(
        string? email,
        bool expected)
    {
        // Arrange
        // Act
        var result = EmailAddressValidation.IsWithinMaximumLength(email);

        // Assert
        Assert.Equal(
            expected,
            result);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("not-an-email", false)]
    [InlineData("Léa <lea@example.fr>", false)]
    [InlineData(" lea@example.fr ", true)]
    public void IsValid_WhenEmailIsProvided_ReturnsExpectedResult(
        string? email,
        bool expected)
    {
        // Arrange
        // Act
        var result = EmailAddressValidation.IsValid(email);

        // Assert
        Assert.Equal(
            expected,
            result);
    }
}
