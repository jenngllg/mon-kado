using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class CompleteGoogleSessionCommandValidatorTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("malformed", false)]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    public async Task ValidateAsync_WhenFlowIsSubmitted_ValidatesBeforeAuthentication(
        string? flow,
        bool expected)
    {
        // Arrange
        var validator = new CompleteGoogleSessionCommandValidator();

        // Act
        var result = await validator.ValidateAsync(
            new CompleteGoogleSessionCommand(flow),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expected,
            result.IsValid);
    }
}
