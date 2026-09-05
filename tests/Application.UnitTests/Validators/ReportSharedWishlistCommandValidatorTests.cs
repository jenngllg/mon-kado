using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class ReportSharedWishlistCommandValidatorTests
{
    private readonly ReportSharedWishlistCommandValidator _validator = new();

    [Theory]
    [InlineData(WishlistReportReason.SpamOrScam)]
    [InlineData(WishlistReportReason.InappropriateContent)]
    [InlineData(WishlistReportReason.PrivacyViolation)]
    public async Task ValidateAsync_WhenKnownReasonHasNoDetails_ReturnsValid(
        WishlistReportReason reason)
    {
        // Arrange
        var command = CreateCommand(
            reason,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ValidateAsync_WhenOtherReasonHasDetails_ReturnsValid()
    {
        // Arrange
        var command = CreateCommand(
            WishlistReportReason.Other,
            "Details");

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ValidateAsync_WhenShareProofIsMissing_ReturnsInvalid(
        bool identifierIsMissing,
        bool secretIsMissing)
    {
        // Arrange
        var command = new ReportSharedWishlistCommand(
            identifierIsMissing
                ? Guid.Empty
                : Guid.CreateVersion7(),
            secretIsMissing
                ? null
                : "secret",
            WishlistReportReason.SpamOrScam,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData((WishlistReportReason)99)]
    public async Task ValidateAsync_WhenReasonIsMissingOrUnknown_ReturnsInvalid(
        WishlistReportReason? reason)
    {
        // Arrange
        var command = CreateCommand(
            reason,
            null);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(ReportSharedWishlistCommand.Reason));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidateAsync_WhenOtherReasonHasBlankDetails_ReturnsInvalid(string? details)
    {
        // Arrange
        var command = CreateCommand(
            WishlistReportReason.Other,
            details);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(ReportSharedWishlistCommand.Details));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ValidateAsync_WhenDetailsAreInvalid_ReturnsInvalid(bool exceedsMaximumLength)
    {
        // Arrange
        var details = exceedsMaximumLength
            ? new string(
                'a',
                WishlistReportTextValidation.MaximumDetailsLength + 1)
            : "Invalid\u0001details";
        var command = CreateCommand(
            WishlistReportReason.SpamOrScam,
            details);

        // Act
        var result = await _validator.ValidateAsync(
            command,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.PropertyName == nameof(ReportSharedWishlistCommand.Details));
    }

    private static ReportSharedWishlistCommand CreateCommand(
        WishlistReportReason? reason,
        string? details)
    {

        return new ReportSharedWishlistCommand(
            Guid.CreateVersion7(),
            "secret",
            reason,
            details);
    }
}
