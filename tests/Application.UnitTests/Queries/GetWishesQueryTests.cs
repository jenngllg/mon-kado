using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetWishesQueryTests
{
    [Fact]
    public void CreateValidationException_WhenIdentifiersAreValid_ReturnsRequestValidationException()
    {
        // Arrange
        var query = new GetWishesQuery(
            Guid.CreateVersion7(),
            Guid.CreateVersion7());
        ValidationError[] errors =
        [
            new ValidationError(
                "unexpected",
                "Invalid.")
        ];

        // Act
        var exception = ((IGenericValidationFailure)query).CreateValidationException(errors);

        // Assert
        var validationException = Assert.IsType<RequestValidationException>(exception);
        Assert.Same(
            errors,
            validationException.ValidationErrors);
    }

    [Theory]
    [InlineData(true, typeof(InvalidAuthenticationSessionException))]
    [InlineData(false, typeof(WishlistNotFoundException))]
    public void CreateValidationException_WhenIdentifierIsEmpty_ReturnsExpectedException(
        bool ownerIsEmpty,
        Type expectedExceptionType)
    {
        // Arrange
        var query = new GetWishesQuery(
            ownerIsEmpty
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
        Assert.IsType(
            expectedExceptionType,
            exception);
    }
}
