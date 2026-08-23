using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class ResetPasswordCommandValidatorTests
{
    private static readonly string _validUserId = Guid.CreateVersion7().ToString("D");
    private readonly ResetPasswordCommandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenRequestIsValid_PreservesPassword()
    {
        // Arrange
        const string NewPassword = " new secure password ";
        var command = new ResetPasswordCommand(
            _validUserId,
            "AbCd_-0123",
            NewPassword);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(
            NewPassword,
            command.NewPassword);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task ValidateAsync_WhenUserIdIsInvalid_ReturnsFailure(string? userId)
    {
        // Arrange
        var command = new ResetPasswordCommand(
            userId,
            "AbCd_-0123",
            "new secure password");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.UserId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("invalid token")]
    [InlineData("invalid+token")]
    public async Task ValidateAsync_WhenTokenIsInvalid_ReturnsFailure(string? token)
    {
        // Arrange
        var command = new ResetPasswordCommand(
            _validUserId,
            token,
            "new secure password");

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
    public async Task ValidateAsync_WhenTokenExceedsMaximumLength_ReturnsFailure()
    {
        // Arrange
        var command = new ResetPasswordCommand(
            _validUserId,
            new string(
                'a',
                2049),
            "new secure password");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Token));
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
        var command = new ResetPasswordCommand(
            _validUserId,
            "AbCd_-0123",
            new string(
                'p',
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
        var command = new ResetPasswordCommand(
            _validUserId,
            "AbCd_-0123",
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
}
