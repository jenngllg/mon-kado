using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class ReportSharedWishlistCommandTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CreateValidationException_WhenShareProofIsInvalid_ReturnsNotFound(
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
        var exception = CreateException(command);

        // Assert
        Assert.IsType<SharedWishlistNotFoundException>(exception);
    }

    [Fact]
    public void CreateValidationException_WhenBodyIsInvalid_ReturnsValidationException()
    {
        // Arrange
        var command = new ReportSharedWishlistCommand(
            Guid.CreateVersion7(),
            "secret",
            null,
            null);

        // Act
        var exception = CreateException(command);

        // Assert
        var validationException = Assert.IsType<RequestValidationException>(exception);
        var validationError = Assert.Single(validationException.ValidationErrors);
        Assert.Equal(
            "reason",
            validationError.PropertyName);
    }

    private static Exception CreateException(ReportSharedWishlistCommand command)
    {

        return ((IGenericValidationFailure)command).CreateValidationException(
        [
            new ValidationError(
                "reason",
                "Invalid.")
        ]);
    }
}
