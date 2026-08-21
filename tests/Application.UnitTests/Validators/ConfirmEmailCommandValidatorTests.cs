using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class ConfirmEmailCommandValidatorTests
{
    private readonly ConfirmEmailCommandValidator _validator = new();

    [Theory]
    [InlineData(
        null,
        "dG9rZW4")]
    [InlineData(
        "",
        "dG9rZW4")]
    [InlineData(
        "not-a-guid",
        "dG9rZW4")]
    [InlineData(
        "00000000-0000-0000-0000-000000000000",
        "dG9rZW4")]
    [InlineData(
        "019c0fd9-7c7f-7de0-b02a-d9a02abc2ab4",
        null)]
    [InlineData(
        "019c0fd9-7c7f-7de0-b02a-d9a02abc2ab4",
        "invalid token")]
    public async Task ValidateAsync_WhenInputIsInvalid_ReturnsFailure(
        string? userId,
        string? token)
    {
        // Arrange
        var command = new ConfirmEmailCommand(
            userId,
            token);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
    }
}
