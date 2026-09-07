using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Validators;

public class UpdateWishlistReportReviewCommandValidatorTests
{
    private readonly UpdateWishlistReportReviewCommandValidator _validator = new();
    [Theory]
    [InlineData(WishlistReportStatus.Pending, null)]
    [InlineData(WishlistReportStatus.Upheld, "note")]
    [InlineData(WishlistReportStatus.Dismissed, " \r\n\t ")]
    public async Task ValidateAsync_WhenReviewIsValid_AcceptsEveryDispositionAndOptionalNote(
        WishlistReportStatus status,
        string? note)
    {
        // Arrange
        var request = new UpdateWishlistReportReviewCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            status,
            note,
            42);

        // Act
        var result = await _validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData((WishlistReportStatus)999)]
    public async Task ValidateAsync_WhenIdentityStatusAndNoteAreInvalid_AggregatesAllFailures(WishlistReportStatus? status)
    {
        // Arrange
        var request = new UpdateWishlistReportReviewCommand(
            Guid.Empty,
            Guid.Empty,
            Guid.Empty,
            status,
            new string(
                'x',
                1001),
            0);

        // Act
        var result = await _validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            [
                "AdministratorId",
                "ReportId",
                "ReviewNote",
                "Status",
                "WishlistId"
            ],
            result.Errors
                .Select(error => error.PropertyName)
                .Order());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0xD800)]
    public async Task ValidateAsync_WhenNoteIsMalformed_RejectsUnsafeText(int codeUnit)
    {
        // Arrange
        var note = new string(
            (char)codeUnit,
            1);
        var request = new UpdateWishlistReportReviewCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            WishlistReportStatus.Upheld,
            note,
            1);

        // Act
        var result = await _validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "ReviewNote",
            Assert
                .Single(result.Errors)
                .PropertyName);
    }

    [Theory]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public async Task ValidateAsync_WhenNoteContainsSupplementaryCharacters_CountsUnicodeScalars(
        int length,
        bool expected)
    {
        // Arrange
        var note = string.Concat(Enumerable.Repeat(
                "\U0001F381",
                length));
        var request = new UpdateWishlistReportReviewCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            WishlistReportStatus.Upheld,
            note,
            1);

        // Act
        var result = await _validator.ValidateAsync(
            request,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            expected,
            result.IsValid);
    }
}
