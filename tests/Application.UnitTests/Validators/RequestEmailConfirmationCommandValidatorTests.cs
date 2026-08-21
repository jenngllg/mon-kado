using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class RequestEmailConfirmationCommandValidatorTests
{
    private readonly RequestEmailConfirmationCommandValidator _validator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("person@example.fr extra")]
    public async Task ValidateAsync_WhenEmailIsInvalid_ReturnsEmailFailure(string? email)
    {
        // Arrange
        var command = new RequestEmailConfirmationCommand(email);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(RequestEmailConfirmationCommand.Email));
    }
}
