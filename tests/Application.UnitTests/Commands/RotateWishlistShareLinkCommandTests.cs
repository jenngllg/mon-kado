using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class RotateWishlistShareLinkCommandTests
{
    [Theory]
    [InlineData(true, false, typeof(InvalidAuthenticationSessionException))]
    [InlineData(false, true, typeof(WishlistShareLinkNotFoundException))]
    public void CreateValidationException_WhenIdentifierIsEmpty_ReturnsExpectedException(
        bool ownerIsEmpty,
        bool wishlistIsEmpty,
        Type expectedExceptionType)
    {
        // Arrange
        var command = new RotateWishlistShareLinkCommand(
            ownerIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            wishlistIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            42);

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
}
