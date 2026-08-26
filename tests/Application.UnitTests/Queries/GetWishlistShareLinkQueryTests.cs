using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishlistShareLinkQueryTests
{
    [Theory]
    [InlineData(true, false, typeof(InvalidAuthenticationSessionException))]
    [InlineData(false, true, typeof(WishlistNotFoundException))]
    public void CreateValidationException_WhenIdentifierIsEmpty_ReturnsExpectedException(
        bool ownerIsEmpty,
        bool wishlistIsEmpty,
        Type expectedExceptionType)
    {
        // Arrange
        var query = new GetWishlistShareLinkQuery(
            ownerIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            wishlistIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7());

        // Act
        var exception = ((IGenericValidationFailure)query).CreateValidationException(
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
