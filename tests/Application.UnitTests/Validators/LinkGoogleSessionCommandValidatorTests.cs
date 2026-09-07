using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class LinkGoogleSessionCommandValidatorTests
{
    [Fact]
    public async Task ValidateAsync_WhenBothInputsAreMissing_ReturnsBothErrors()
    {
        // Arrange
        var validator = new LinkGoogleSessionCommandValidator();
        var request = new LinkGoogleSessionCommand(
            null,
            null);

        // Act
        var result = await validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                "Flow",
                "CurrentPassword"
            ],
            result.Errors.Select(error => error.PropertyName));
    }
}
