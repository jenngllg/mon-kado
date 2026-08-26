using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Behaviors;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

namespace JennGllg.Fr.MonKado.Back.Application.UnitTests.Commands;

public class JoinSharedWishlistCommandTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CreateValidationException_WhenShareProofIsInvalid_ReturnsNotFound(
        bool identifierIsMissing,
        bool secretIsMissing)
    {
        // Arrange
        var command = new JoinSharedWishlistCommand(
            identifierIsMissing
                ? Guid.Empty
                : Guid.CreateVersion7(),
            secretIsMissing
                ? null
                : "secret",
            null,
            null,
            "Jenn");

        // Act
        var exception = CreateException(command);

        // Assert
        Assert.IsType<SharedWishlistNotFoundException>(exception);
    }

    [Fact]
    public void CreateValidationException_WhenMemberIsEmpty_ReturnsInvalidSession()
    {
        // Arrange
        var command = new JoinSharedWishlistCommand(
            Guid.CreateVersion7(),
            "secret",
            Guid.Empty,
            "guest",
            null);

        // Act
        var exception = CreateException(command);

        // Assert
        Assert.IsType<InvalidAuthenticationSessionException>(exception);
    }

    [Fact]
    public void CreateValidationException_WhenDisplayNameIsInvalid_ReturnsValidationException()
    {
        // Arrange
        var command = new JoinSharedWishlistCommand(
            Guid.CreateVersion7(),
            "secret",
            null,
            "guest",
            null);

        // Act
        var exception = CreateException(command);

        // Assert
        var validationException = Assert.IsType<RequestValidationException>(exception);
        Assert.Single(validationException.ValidationErrors);
        Assert.Equal(
            "displayName",
            validationException.ValidationErrors.Single().PropertyName);
    }

    private static Exception CreateException(JoinSharedWishlistCommand command)
    {
        return ((IGenericValidationFailure)command).CreateValidationException(
        [
            new ValidationError(
                "displayName",
                "Invalid.")
        ]);
    }
}
