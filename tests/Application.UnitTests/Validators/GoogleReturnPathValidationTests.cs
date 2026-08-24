using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class GoogleReturnPathValidationTests
{
    [Fact]
    public void IsCanonical_WhenReturnPathIsNull_ReturnsFalse()
    {
        // Arrange
        var returnPath = default(string);

        // Act
        var isCanonical = GoogleReturnPathValidation.IsCanonical(returnPath);

        // Assert
        Assert.False(isCanonical);
    }

    [Fact]
    public void IsCanonical_WhenReturnPathExceedsMaximumLength_ReturnsFalse()
    {
        // Arrange
        var returnPath = $"/{new string('a', GoogleReturnPathValidation.MaximumLength)}";

        // Act
        var isCanonical = GoogleReturnPathValidation.IsCanonical(returnPath);

        // Assert
        Assert.False(isCanonical);
    }
}
