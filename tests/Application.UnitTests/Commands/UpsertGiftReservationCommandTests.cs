using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class UpsertGiftReservationCommandTests
{
    [Theory]
    [InlineData(true, false, false, typeof(SharedWishlistNotFoundException))]
    [InlineData(false, true, false, typeof(WishNotFoundException))]
    [InlineData(false, false, true, typeof(InvalidAuthenticationSessionException))]
    [InlineData(false, false, false, typeof(RequestValidationException))]
    public void CreateValidationException_WhenValidationFails_ReturnsExpectedException(
        bool shareLinkIsInvalid,
        bool wishIdIsEmpty,
        bool memberIdIsEmpty,
        Type expectedExceptionType)
    {
        // Arrange
        var command = new UpsertGiftReservationCommand(
            shareLinkIsInvalid
                ? Guid.Empty
                : Guid.CreateVersion7(),
            shareLinkIsInvalid
                ? null
                : "secret",
            wishIdIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            memberIdIsEmpty
                ? Guid.Empty
                : null,
            "guest",
            1,
            null);

        // Act
        var exception = ((IGenericValidationFailure)command).CreateValidationException(
        [
            new ValidationError(
                "quantity",
                "Invalid.")
        ]);

        // Assert
        Assert.IsType(
            expectedExceptionType,
            exception);
    }
}
