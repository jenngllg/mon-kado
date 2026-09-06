using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class UpdateWishlistModerationCommandValidatorTests
{
    private readonly UpdateWishlistModerationCommandValidator _validator = new();
    [Theory]
    [InlineData(true, "Reason", true)]
    [InlineData(true, "  Reason\nwith details  ", true)]
    [InlineData(true, "Reason\twith details\r\nMore", true)]
    [InlineData(true, null, false)]
    [InlineData(true, "", false)]
    [InlineData(true, " \t\r\n ", false)]
    [InlineData(true, "Hidden\u0000value", false)]
    [InlineData(false, null, true)]
    [InlineData(false, "", false)]
    [InlineData(false, "Reason", false)]
    [InlineData(null, null, false)]
    public async Task ValidateAsync_WhenStateAndReasonAreProvided_EnforcesConditionalRules(
        bool? isSuspended,
        string? reason,
        bool expectedValid)
    {
        // Arrange
        var command = new UpdateWishlistModerationCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            isSuspended,
            reason,
            1);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedValid,
            result.IsValid);
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public async Task ValidateAsync_WhenReasonReachesLimit_CountsUnicodeScalars(
        int length,
        bool expectedValid)
    {
        // Arrange
        var reason = string.Concat(Enumerable.Repeat(
                "🎁",
                length));
        var command = new UpdateWishlistModerationCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            true,
            reason,
            1);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expectedValid,
            result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenUnicodeIsMalformed_ReturnsFailure()
    {
        // Arrange
        var command = new UpdateWishlistModerationCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            true,
            new string(
                (char)0xd800,
                1),
            1);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
        Assert.Equal(
            "Reason",
            Assert
                .Single(result.Errors)
                .PropertyName);
    }

    [Fact]
    public async Task ValidateAsync_WhenIdentifiersAndStateAreMissing_AggregatesErrors()
    {
        // Arrange
        var command = new UpdateWishlistModerationCommand(
            Guid.Empty,
            Guid.Empty,
            null,
            null,
            1);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            3,
            result.Errors.Count);
    }
}
