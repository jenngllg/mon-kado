using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class UpdateWishCommandTests
{
    [Theory]
    [InlineData(true, false, false, typeof(InvalidAuthenticationSessionException))]
    [InlineData(false, true, false, typeof(WishlistNotFoundException))]
    [InlineData(false, false, true, typeof(WishNotFoundException))]
    [InlineData(false, false, false, typeof(RequestValidationException))]
    public void CreateValidationException_WhenValidationFails_ReturnsExpectedException(
        bool ownerIsEmpty,
        bool wishlistIsEmpty,
        bool wishIsEmpty,
        Type expectedExceptionType)
    {
        // Arrange
        var command = new UpdateWishCommand(
            ownerIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            wishlistIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            wishIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            null,
            null,
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
