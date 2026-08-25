using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishQueryTests
{
    [Theory]
    [InlineData(true, typeof(InvalidAuthenticationSessionException))]
    [InlineData(false, typeof(WishNotFoundException))]
    public void CreateValidationException_WhenValidationFails_ReturnsExpectedException(
        bool memberIsEmpty,
        Type expectedExceptionType)
    {
        // Arrange
        var query = new GetWishQuery(
            memberIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            Guid.Empty,
            Guid.Empty);
        ValidationError[] errors =
        [
            new ValidationError(
                "wishlistId",
                "Invalid.")
        ];

        // Act
        var exception = ((IGenericValidationFailure)query).CreateValidationException(errors);

        // Assert
        Assert.IsType(
            expectedExceptionType,
            exception);
    }
}
