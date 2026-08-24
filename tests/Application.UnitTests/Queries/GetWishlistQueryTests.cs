using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishlistQueryTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreateValidationException_WhenValidationFails_ReturnsExpectedException(bool memberIsEmpty)
    {
        // Arrange
        var query = new GetWishlistQuery(
            memberIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
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
        if (memberIsEmpty)
            Assert.IsType<InvalidAuthenticationSessionException>(exception);
        else
            Assert.IsType<WishlistNotFoundException>(exception);
    }
}
