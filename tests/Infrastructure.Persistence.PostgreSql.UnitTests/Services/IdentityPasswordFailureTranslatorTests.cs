using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.UnitTests.Services;

public class IdentityPasswordFailureTranslatorTests
{
    [Fact]
    public void CreateChangeException_WhenCurrentPasswordMismatches_ReturnsCurrentPasswordInvalid()
    {
        // Arrange
        var result = IdentityResult.Failed(CreateError(
            "PasswordMismatch",
            "Password mismatch."));

        // Act
        var exception = IdentityPasswordFailureTranslator.CreateChangeException(result);

        // Assert
        Assert.IsType<CurrentPasswordInvalidException>(exception);
    }

    [Theory]
    [InlineData("PasswordTooShort", ValidationMessages.PasswordTooShort)]
    [InlineData("PasswordTooLong", ValidationMessages.PasswordTooLong)]
    [InlineData("PasswordRequiresUniqueChars", "Unique characters are required.")]
    [InlineData("PasswordRequiresNonAlphanumeric", "A symbol is required.")]
    [InlineData("PasswordRequiresDigit", "A digit is required.")]
    [InlineData("PasswordRequiresLower", "A lowercase character is required.")]
    [InlineData("PasswordRequiresUpper", "An uppercase character is required.")]
    public void CreateChangeException_WhenPasswordPolicyFails_ReturnsValidationException(
        string code,
        string expectedMessage)
    {
        // Arrange
        var result = IdentityResult.Failed(CreateError(
            code,
            expectedMessage));

        // Act
        var exception = IdentityPasswordFailureTranslator.CreateChangeException(result);

        // Assert
        var validationException = Assert.IsType<RequestValidationException>(exception);
        var error = Assert.Single(validationException.ValidationErrors);
        Assert.Equal(
            "newPassword",
            error.PropertyName);
        Assert.Equal(
            expectedMessage,
            error.ErrorMessage);
    }

    [Fact]
    public void CreateChangeException_WhenIdentityFailureIsUnexpected_ReturnsInvalidOperation()
    {
        // Arrange
        var result = IdentityResult.Failed(CreateError(
            "Unexpected",
            "Unexpected failure."));

        // Act
        var exception = IdentityPasswordFailureTranslator.CreateChangeException(result);

        // Assert
        Assert.IsType<InvalidOperationException>(exception);
    }

    [Fact]
    public void HandleResetFailure_WhenTokenIsInvalid_ReturnsFalse()
    {
        // Arrange
        var result = IdentityResult.Failed(CreateError(
            "InvalidToken",
            "Invalid token."));

        // Act
        var handled = IdentityPasswordFailureTranslator.HandleResetFailure(result);

        // Assert
        Assert.False(handled);
    }

    [Fact]
    public void HandleResetFailure_WhenPasswordPolicyFails_ThrowsValidationException()
    {
        // Arrange
        var result = IdentityResult.Failed(CreateError(
            "PasswordTooShort",
            "Password is too short."));

        // Act
        var action = () =>
        {
            _ = IdentityPasswordFailureTranslator.HandleResetFailure(result);
        };

        // Assert
        var exception = Assert.Throws<RequestValidationException>(action);
        var error = Assert.Single(exception.ValidationErrors);
        Assert.Equal(
            ValidationMessages.PasswordTooShort,
            error.ErrorMessage);
    }

    [Fact]
    public void HandleResetFailure_WhenIdentityConcurrencyFails_ThrowsResetInvalid()
    {
        // Arrange
        var result = IdentityResult.Failed(CreateError(
            "ConcurrencyFailure",
            "Concurrency failure."));

        // Act
        var action = () =>
        {
            _ = IdentityPasswordFailureTranslator.HandleResetFailure(result);
        };

        // Assert
        Assert.Throws<PasswordResetInvalidException>(action);
    }

    [Fact]
    public void HandleResetFailure_WhenIdentityFailureIsUnexpected_ThrowsInvalidOperation()
    {
        // Arrange
        var result = IdentityResult.Failed(CreateError(
            "Unexpected",
            "Unexpected failure."));

        // Act
        var action = () =>
        {
            _ = IdentityPasswordFailureTranslator.HandleResetFailure(result);
        };

        // Assert
        Assert.Throws<InvalidOperationException>(action);
    }

    private static IdentityError CreateError(
        string code,
        string description)
    {

        return new IdentityError
        {
            Code = code,
            Description = description
        };
    }
}
