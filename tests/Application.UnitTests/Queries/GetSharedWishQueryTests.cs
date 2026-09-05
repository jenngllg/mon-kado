using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;
using JennGllg.Fr.MonKado.Back.Application.Queries;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Queries;

public class GetSharedWishQueryTests
{
    [Theory]
    [InlineData(true, false, false, false, typeof(RequestValidationException))]
    [InlineData(false, true, false, false, typeof(SharedWishlistNotFoundException))]
    [InlineData(false, false, true, false, typeof(RequestValidationException))]
    [InlineData(false, false, false, true, typeof(InvalidAuthenticationSessionException))]
    [InlineData(false, false, false, false, typeof(RequestValidationException))]
    public void CreateValidationException_WhenQueryIsInvalid_ReturnsExpectedException(
        bool shareLinkIdIsEmpty,
        bool secretIsMissing,
        bool wishIdIsEmpty,
        bool memberIdIsEmpty,
        Type expectedExceptionType)
    {
        // Arrange
        var query = new GetSharedWishQuery(
            shareLinkIdIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            secretIsMissing
                ? null
                : "secret",
            wishIdIsEmpty
                ? Guid.Empty
                : Guid.CreateVersion7(),
            memberIdIsEmpty
                ? Guid.Empty
                : null,
            null);

        // Act
        var exception = ((IGenericValidationFailure)query).CreateValidationException(
        [
            new ValidationError(
                "property",
                "Invalid.")
        ]);

        // Assert
        Assert.IsType(
            expectedExceptionType,
            exception);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void CreateValidationException_WhenSecretIsBlank_ReturnsSharedWishlistNotFoundException(
        string secret)
    {
        // Arrange
        var query = new GetSharedWishQuery(
            Guid.CreateVersion7(),
            secret,
            Guid.CreateVersion7(),
            null,
            null);

        // Act
        var exception = ((IGenericValidationFailure)query).CreateValidationException([]);

        // Assert
        Assert.IsType<SharedWishlistNotFoundException>(exception);
    }

    [Fact]
    public void CreateValidationException_WhenMemberIdIsPresent_ReturnsRequestValidationException()
    {
        // Arrange
        var query = new GetSharedWishQuery(
            Guid.CreateVersion7(),
            "secret",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            null);

        // Act
        var exception = ((IGenericValidationFailure)query).CreateValidationException([]);

        // Assert
        Assert.IsType<RequestValidationException>(exception);
    }
}
