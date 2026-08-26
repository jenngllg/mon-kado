using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetCurrentWishlistParticipantQueryTests
{
    [Fact]
    public void CreateValidationException_WhenQueryIsInvalid_ReturnsSharedWishlistNotFound()
    {
        // Arrange
        var query = new GetCurrentWishlistParticipantQuery(
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
