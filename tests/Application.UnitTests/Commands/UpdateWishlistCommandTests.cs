using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class UpdateWishlistCommandTests
{
    [Theory]
    [InlineData(true, false, typeof(InvalidAuthenticationSessionException))]
    [InlineData(false, true, typeof(WishlistNotFoundException))]
    [InlineData(false, false, typeof(RequestValidationException))]
    public void CreateValidationException_WhenValidationFails_ReturnsExpectedException(
        bool ownerIsEmpty,
        bool wishlistIsEmpty,
        Type expectedExceptionType)
    {
        // Arrange
        var command = new UpdateWishlistCommand(
            ownerIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            wishlistIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            null,
            WishlistOccasion.Other,
            null,
            null,
            42);
        ValidationError[] errors =
        [
            new ValidationError(
                "name",
                "Invalid.")
        ];

        // Act
        var exception = ((IGenericValidationFailure)command).CreateValidationException(errors);

        // Assert
        Assert.IsType(
            expectedExceptionType,
            exception);
    }
}
