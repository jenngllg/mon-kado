using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class RequestMemberEmailChangeCommandValidatorTests
{
    private readonly RequestMemberEmailChangeCommandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenRequestIsValid_ReturnsSuccess()
    {
        // Arrange
        var command = new RequestMemberEmailChangeCommand(
            Guid.CreateVersion7(),
            " new@example.fr ",
            "current-password",
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("a@")]
    public async Task ValidateAsync_WhenEmailIsInvalid_ReturnsEmailFailure(string? email)
    {
        // Arrange
        var command = new RequestMemberEmailChangeCommand(
            Guid.CreateVersion7(),
            email,
            "current-password",
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Email));
    }

    [Fact]
    public async Task ValidateAsync_WhenEmailExceedsMaximumLength_ReturnsEmailFailure()
    {
        // Arrange
        var command = new RequestMemberEmailChangeCommand(
            Guid.CreateVersion7(),
            $"{new string(
                'a',
                250)}@example.fr",
            "current-password",
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Email));
    }

    [Fact]
    public async Task ValidateAsync_WhenMemberIdAndPasswordAreMissing_ReturnsBothFailures()
    {
        // Arrange
        var command = new RequestMemberEmailChangeCommand(
            Guid.Empty,
            "new@example.fr",
            null,
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.MemberId));
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.CurrentPassword));
    }
}
