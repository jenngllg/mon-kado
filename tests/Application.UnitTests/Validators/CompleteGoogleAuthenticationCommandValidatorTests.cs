using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class CompleteGoogleAuthenticationCommandValidatorTests
{
    private readonly CompleteGoogleAuthenticationCommandValidator _validator;

    public CompleteGoogleAuthenticationCommandValidatorTests()
    {
        _validator = new CompleteGoogleAuthenticationCommandValidator();
    }

    [Fact]
    public async Task ValidateAsync_WhenCommandIsValid_ReturnsValid()
    {
        // Arrange
        var expectedMemberId = Guid.CreateVersion7();
        var currentSessionId = Guid.CreateVersion7();
        var command = new CompleteGoogleAuthenticationCommand(
            GoogleAuthenticationValidatorTestData.CreateValidIdentity(),
            false,
            "/my-lists",
            Guid.CreateVersion7(),
            expectedMemberId,
            currentSessionId);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenContextIsInvalid_AggregatesFailures()
    {
        // Arrange
        var command = new CompleteGoogleAuthenticationCommand(
            null,
            false,
            "https://evil.example",
            Guid.Empty,
            Guid.Empty,
            Guid.Empty);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.Identity));
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.ReturnPath));
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.FlowId));
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.ExpectedMemberId));
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.CurrentSessionId));
    }

    [Theory]
    [InlineData("/my-lists/")]
    [InlineData("/my//lists")]
    [InlineData("/my/./lists")]
    [InlineData("/my/../lists")]
    public async Task ValidateAsync_WhenReturnPathIsNotCanonical_ReturnsReturnPathFailure(
        string returnPath)
    {
        // Arrange
        var command = new CompleteGoogleAuthenticationCommand(
            GoogleAuthenticationValidatorTestData.CreateValidIdentity(),
            false,
            returnPath,
            Guid.CreateVersion7(),
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.ReturnPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("//evil.example")]
    [InlineData("/my%2Flists")]
    [InlineData("/my\\lists")]
    [InlineData("/my#lists")]
    [InlineData("/my?lists")]
    [InlineData("/my lists")]
    [InlineData("/my\u0001lists")]
    public async Task ValidateAsync_WhenReturnPathContainsForbiddenForm_ReturnsReturnPathFailure(
        string? returnPath)
    {
        // Arrange
        var command = new CompleteGoogleAuthenticationCommand(
            GoogleAuthenticationValidatorTestData.CreateValidIdentity(),
            false,
            returnPath,
            Guid.CreateVersion7(),
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.ReturnPath));
    }

    [Fact]
    public async Task ValidateAsync_WhenReturnPathIsTooLong_ReturnsReturnPathFailure()
    {
        // Arrange
        var command = new CompleteGoogleAuthenticationCommand(
            GoogleAuthenticationValidatorTestData.CreateValidIdentity(),
            false,
            $"/{new string('a', 256)}",
            Guid.CreateVersion7(),
            null,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.ReturnPath));
    }
}
