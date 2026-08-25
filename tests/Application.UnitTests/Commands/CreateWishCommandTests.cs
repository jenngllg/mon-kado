using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CreateWishCommandTests
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
        var command = new CreateWishCommand(
            ownerIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            wishlistIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            null,
            null,
            null,
            null);
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
