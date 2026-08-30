using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetCurrentGiftReservationQueryTests
{
    [Theory]
    [InlineData(true, false, false, typeof(SharedWishlistNotFoundException))]
    [InlineData(false, true, false, typeof(GiftReservationNotFoundException))]
    [InlineData(false, false, true, typeof(InvalidAuthenticationSessionException))]
    [InlineData(false, false, false, typeof(GuestSessionInvalidException))]
    public void CreateValidationException_WhenValidationFails_ReturnsExpectedException(
        bool shareLinkIsInvalid,
        bool wishIdIsEmpty,
        bool memberIdIsEmpty,
        Type expectedExceptionType)
    {
        // Arrange
        var query = new GetCurrentGiftReservationQuery(
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
            "guest");

        // Act
        var exception = ((IGenericValidationFailure)query).CreateValidationException(
        [
            new ValidationError(
                "wishId",
                "Invalid.")
        ]);

        // Assert
        Assert.IsType(
            expectedExceptionType,
            exception);
    }
}
