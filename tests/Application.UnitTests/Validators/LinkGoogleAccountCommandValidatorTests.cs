using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class LinkGoogleAccountCommandValidatorTests
{
    private readonly LinkGoogleAccountCommandValidator _validator;

    public LinkGoogleAccountCommandValidatorTests()
    {
        _validator = new LinkGoogleAccountCommandValidator();
    }

    [Fact]
    public async Task ValidateAsync_WhenPasswordContainsWhitespace_PreservesAndAcceptsPassword()
    {
        // Arrange
        var expectedMemberId = Guid.CreateVersion7();
        var currentSessionId = Guid.CreateVersion7();
        var command = new LinkGoogleAccountCommand(
            GoogleAuthenticationValidatorTestData.CreateValidIdentity(),
            false,
            "/",
            Guid.CreateVersion7(),
            expectedMemberId,
            currentSessionId,
            "  exact password  ");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
        Assert.Equal(
            "  exact password  ",
            command.CurrentPassword);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task ValidateAsync_WhenPasswordIsMissing_ReturnsPasswordFailure(
        string? password)
    {
        // Arrange
        var command = new LinkGoogleAccountCommand(
            GoogleAuthenticationValidatorTestData.CreateValidIdentity(),
            false,
            "/",
            Guid.CreateVersion7(),
            null,
            null,
            password);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.CurrentPassword));
    }

    [Fact]
    public async Task ValidateAsync_WhenContextIsInvalid_AggregatesFailures()
    {
        // Arrange
        var command = new LinkGoogleAccountCommand(
            null,
            false,
            "https://evil.example",
            Guid.Empty,
            Guid.Empty,
            Guid.Empty,
            null);

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
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.CurrentPassword));
    }
}
