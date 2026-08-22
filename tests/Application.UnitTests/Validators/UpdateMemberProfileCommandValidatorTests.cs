using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class UpdateMemberProfileCommandValidatorTests
{
    private readonly UpdateMemberProfileCommandValidator _validator = new();

    [Fact]
    public async Task ValidateAsync_WhenDisplayNameIsValid_ReturnsSuccess()
    {
        // Arrange
        var command = new UpdateMemberProfileCommand(
            Guid.CreateVersion7(),
            " Jenn ",
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenMemberIdIsEmpty_ReturnsMemberIdFailure()
    {
        // Arrange
        var command = new UpdateMemberProfileCommand(
            Guid.Empty,
            "Jenn",
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.MemberId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Jenn\nMartin")]
    public async Task ValidateAsync_WhenDisplayNameIsInvalid_ReturnsDisplayNameFailure(
        string? displayName)
    {
        // Arrange
        var command = new UpdateMemberProfileCommand(
            Guid.CreateVersion7(),
            displayName,
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.DisplayName));
    }

    [Fact]
    public async Task ValidateAsync_WhenDisplayNameExceedsMaximumLength_ReturnsDisplayNameFailure()
    {
        // Arrange
        var command = new UpdateMemberProfileCommand(
            Guid.CreateVersion7(),
            new string(
                'a',
                81),
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.DisplayName));
    }

    [Fact]
    public async Task ValidateAsync_WhenDisplayNameContainsUnpairedSurrogate_ReturnsDisplayNameFailure()
    {
        // Arrange
        var command = new UpdateMemberProfileCommand(
            Guid.CreateVersion7(),
            new string(
                (char)0xD800,
                1),
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(command.DisplayName));
    }

    [Fact]
    public async Task ValidateAsync_WhenDisplayNameContainsValidSurrogatePair_ReturnsSuccess()
    {
        // Arrange
        var command = new UpdateMemberProfileCommand(
            Guid.CreateVersion7(),
            "Jenn 🎁",
            42);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }
}
