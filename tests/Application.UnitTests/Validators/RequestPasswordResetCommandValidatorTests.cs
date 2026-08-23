using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class RequestPasswordResetCommandValidatorTests
{
    private readonly RequestPasswordResetCommandValidator _validator = new();

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("invalid", false)]
    [InlineData("member@example.fr", true)]
    [InlineData(" member@example.fr ", true)]
    public async Task ValidateAsync_WhenEmailVaries_ReturnsExpectedResult(
        string? email,
        bool expectedIsValid)
    {
        // Arrange
        var command = new RequestPasswordResetCommand(email);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedIsValid,
            result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenEmailExceedsMaximumLength_ReturnsFailure()
    {
        // Arrange
        var command = new RequestPasswordResetCommand(
            new string(
                'a',
                250) + "@x.fr");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
    }
}
