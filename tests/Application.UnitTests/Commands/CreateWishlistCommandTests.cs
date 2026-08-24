using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class CreateWishlistCommandTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateValidationException_WhenValidationFails_ReturnsExpectedException(bool ownerIsEmpty)
    {
        // Arrange
        var command = new CreateWishlistCommand(
            ownerIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            null,
            WishlistOccasion.Other,
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
        if (ownerIsEmpty)
            Assert.IsType<InvalidAuthenticationSessionException>(exception);
        else
            Assert.IsType<RequestValidationException>(exception);
    }
}
