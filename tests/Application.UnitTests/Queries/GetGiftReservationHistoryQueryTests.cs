using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetGiftReservationHistoryQueryTests
{
    [Fact]
    public void CreateValidationException_WhenMemberIdIsInvalid_ReturnsInvalidAuthenticationSessionException()
    {
        // Arrange
        var query = new GetGiftReservationHistoryQuery(
            Guid.Empty,
            1,
            20,
            null);
        var errors = new[]
        {
            new ValidationError(
                "memberId",
                "Invalid.")
        };

        // Act
        var exception = ((IGenericValidationFailure)query).CreateValidationException(errors);

        // Assert
        Assert.IsType<InvalidAuthenticationSessionException>(exception);
    }

    [Fact]
    public void CreateValidationException_WhenQueryParameterIsInvalid_ReturnsRequestValidationException()
    {
        // Arrange
        var query = new GetGiftReservationHistoryQuery(
            Guid.CreateVersion7(),
            0,
            20,
            null);
        var errors = new[]
        {
            new ValidationError(
                "page",
                "Invalid.")
        };

        // Act
        var exception = ((IGenericValidationFailure)query).CreateValidationException(errors);

        // Assert
        var validationException = Assert.IsType<RequestValidationException>(exception);
        Assert.Equal(
            errors,
            validationException.ValidationErrors);
    }
}
