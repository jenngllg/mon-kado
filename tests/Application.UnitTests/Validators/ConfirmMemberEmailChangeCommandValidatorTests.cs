using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class ConfirmMemberEmailChangeCommandValidatorTests
{
    private readonly ConfirmMemberEmailChangeCommandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenConfirmationIsValid_ReturnsSuccess()
    {
        // Arrange
        var command = new ConfirmMemberEmailChangeCommand(
            Guid.CreateVersion7(),
            "AbCd_-0123");

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
    [InlineData("contains+invalid")]
    public async Task ValidateAsync_WhenTokenIsInvalid_ReturnsTokenFailure(string? token)
    {
        // Arrange
        var command = new ConfirmMemberEmailChangeCommand(
            Guid.CreateVersion7(),
            token);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Token));
    }

    [Fact]
    public async Task ValidateAsync_WhenRequestIdIsMissing_ReturnsRequestIdFailure()
    {
        // Arrange
        var command = new ConfirmMemberEmailChangeCommand(
            null,
            "AbCd_-0123");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.RequestId));
    }

    [Fact]
    public async Task ValidateAsync_WhenTokenExceedsMaximumLength_ReturnsTokenFailure()
    {
        // Arrange
        var command = new ConfirmMemberEmailChangeCommand(
            Guid.CreateVersion7(),
            new string(
                'a',
                2049));

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Token));
    }
}
