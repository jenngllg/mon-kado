using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class UpdateMemberPasswordCommandValidatorTests
{
    private readonly UpdateMemberPasswordCommandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenRequestIsValid_PreservesExactPasswords()
    {
        // Arrange
        var command = new UpdateMemberPasswordCommand(
            Guid.CreateVersion7(),
            " current password ",
            " new secure password ");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(
            " current password ",
            command.CurrentPassword);
        Assert.Equal(
            " new secure password ",
            command.NewPassword);
    }

    [Fact]
    public async Task ValidateAsync_WhenAllValuesAreMissing_ReturnsEveryPropertyFailure()
    {
        // Arrange
        var command = new UpdateMemberPasswordCommand(
            Guid.Empty,
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                nameof(command.MemberId),
                nameof(command.CurrentPassword),
                nameof(command.NewPassword)
            ],
            result.Errors.Select(error => error.PropertyName));
    }

    [Fact]
    public async Task ValidateAsync_WhenCurrentPasswordExceedsMaximumLength_ReturnsCurrentPasswordFailure()
    {
        // Arrange
        var command = new UpdateMemberPasswordCommand(
            Guid.CreateVersion7(),
            new string(
                'a',
                129),
            "new secure password");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            nameof(command.CurrentPassword),
            error.PropertyName);
        Assert.Equal(
            ValidationMessages.PasswordTooLong,
            error.ErrorMessage);
    }

    [Theory]
    [InlineData(11, false)]
    [InlineData(12, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public async Task ValidateAsync_WhenNewPasswordLengthVaries_ReturnsExpectedResult(
        int length,
        bool expectedIsValid)
    {
        // Arrange
        var command = new UpdateMemberPasswordCommand(
            Guid.CreateVersion7(),
            "current password",
            new string(
                'n',
                length));

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
    public async Task ValidateAsync_WhenNewPasswordUsesUnicodeScalars_CountsScalarsInsteadOfUtf16Units()
    {
        // Arrange
        var command = new UpdateMemberPasswordCommand(
            Guid.CreateVersion7(),
            "current password",
            string.Concat(Enumerable.Repeat(
                "🎁",
                12)));

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenNewPasswordEqualsCurrentPassword_ReturnsDifferenceFailure()
    {
        // Arrange
        var command = new UpdateMemberPasswordCommand(
            Guid.CreateVersion7(),
            "same password",
            "same password");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        var error = Assert.Single(result.Errors);
        Assert.Equal(
            nameof(command.NewPassword),
            error.PropertyName);
        Assert.Equal(
            ValidationMessages.NewPasswordMustDiffer,
            error.ErrorMessage);
    }
}
