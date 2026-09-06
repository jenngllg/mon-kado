using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class UpsertWishImageCommandTests
{
    [Theory]
    [InlineData(true, false, false, typeof(InvalidAuthenticationSessionException))]
    [InlineData(false, true, false, typeof(WishlistNotFoundException))]
    [InlineData(false, false, true, typeof(WishNotFoundException))]
    public void CreateValidationException_WhenIdentifierIsEmpty_ReturnsExpectedException(
        bool ownerIsEmpty,
        bool wishlistIsEmpty,
        bool wishIsEmpty,
        Type expectedExceptionType)
    {
        // Arrange
        var command = new UpsertWishImageCommand(
            ownerIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            wishlistIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            wishIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            [1],
            42,
            true);

        // Act
        var exception = ((IGenericValidationFailure)command).CreateValidationException(
        [
            new ValidationError(
                "identifier",
                "Invalid.")
        ]);

        // Assert
        Assert.IsType(
            expectedExceptionType,
            exception);
    }

    [Fact]
    public void CreateValidationException_WhenIdentifiersAreValid_ReturnsRequestValidationException()
    {
        // Arrange
        var command = new UpsertWishImageCommand(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null,
            42,
            false);
        var validationError = new ValidationError(
            "image",
            "Invalid.");

        // Act
        var exception = ((IGenericValidationFailure)command).CreateValidationException(
        [
            validationError
        ]);

        // Assert
        var validationException = Assert.IsType<RequestValidationException>(exception);
        Assert.Empty(command.Image);
        Assert.Equal(
            validationError,
            Assert.Single(validationException.ValidationErrors));
    }
}
