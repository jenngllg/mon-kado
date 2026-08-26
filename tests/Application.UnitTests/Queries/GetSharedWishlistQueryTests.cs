using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetSharedWishlistQueryTests
{
    [Fact]
    public void CreateValidationException_WhenQueryIsInvalid_ReturnsSharedWishlistNotFoundException()
    {
        // Arrange
        var query = new GetSharedWishlistQuery(
            Guid.Empty,
            null,
            null,
            null);

        // Act
        var exception = ((IGenericValidationFailure)query).CreateValidationException(
        [
            new ValidationError(
                "secret",
                "Invalid.")
        ]);

        // Assert
        Assert.IsType<SharedWishlistNotFoundException>(exception);
    }
}
