using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishlistsQueryTests
{
    [Fact]
    public void CreateValidationException_WhenValidationFails_ReturnsInvalidAuthenticationSessionException()
    {
        // Arrange
        var query = new GetWishlistsQuery(Guid.Empty);
        ValidationError[] errors =
        [
            new ValidationError(
                "memberId",
                "Invalid.")
        ];

        // Act
        var exception = ((IGenericValidationFailure)query).CreateValidationException(errors);

        // Assert
        Assert.IsType<InvalidAuthenticationSessionException>(exception);
    }
}
