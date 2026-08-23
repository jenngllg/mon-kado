using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Translates Identity password failures into application results and exceptions.
/// </summary>
public static class IdentityPasswordFailureTranslator
{
    private static readonly HashSet<string> _passwordPolicyErrorCodes =
    [
        "PasswordTooShort",
        "PasswordTooLong",
        "PasswordRequiresUniqueChars",
        "PasswordRequiresNonAlphanumeric",
        "PasswordRequiresDigit",
        "PasswordRequiresLower",
        "PasswordRequiresUpper"
    ];

    /// <summary>
    /// Translates an authenticated password change failure.
    /// </summary>
    /// <param name="result">The failed Identity result.</param>
    /// <returns>The application exception representing the failure.</returns>
    public static Exception CreateChangeException(IdentityResult result)
    {
        var errors = result.Errors.ToArray();

        if (errors.Any(error => error.Code == "PasswordMismatch"))
            return new CurrentPasswordInvalidException();

        var validationException = CreatePasswordValidationException(errors);

        if (validationException is not null)
            return validationException;

        return new InvalidOperationException("Identity could not change the member password.");
    }

    /// <summary>
    /// Translates a failed anonymous password reset result.
    /// </summary>
    /// <param name="result">The failed Identity result.</param>
    /// <returns><see langword="false" /> when the reset token is invalid.</returns>
    /// <exception cref="RequestValidationException">Thrown when Identity rejects the new password.</exception>
    /// <exception cref="PasswordResetInvalidException">Thrown when Identity concurrency fails.</exception>
    /// <exception cref="InvalidOperationException">Thrown when Identity reports an unexpected failure.</exception>
    public static bool HandleResetFailure(IdentityResult result)
    {
        var errors = result.Errors.ToArray();

        if (errors.Any(error => error.Code == "InvalidToken"))
            return false;

        var validationException = CreatePasswordValidationException(errors);

        if (validationException is not null)
            throw validationException;

        if (errors.Any(error => error.Code == "ConcurrencyFailure"))
            throw new PasswordResetInvalidException();

        throw new InvalidOperationException("Identity could not reset the member password.");
    }

    /// <summary>
    /// Creates an aggregated application validation exception for Identity password policy errors.
    /// </summary>
    /// <param name="errors">The Identity errors.</param>
    /// <returns>The validation exception when password policy errors exist; otherwise, <see langword="null" />.</returns>
    private static RequestValidationException? CreatePasswordValidationException(
        IEnumerable<IdentityError> errors)
    {
        var passwordErrors = errors
            .Where(error => _passwordPolicyErrorCodes.Contains(error.Code))
            .Select(error => new ValidationError(
                "newPassword",
                GetPasswordPolicyMessage(error)))
            .ToArray();

        return passwordErrors.Length == 0
            ? null
            : new RequestValidationException(passwordErrors);
    }

    /// <summary>
    /// Gets the client-facing validation message for an Identity password policy error.
    /// </summary>
    /// <param name="error">The Identity password policy error.</param>
    /// <returns>The validation message.</returns>
    private static string GetPasswordPolicyMessage(IdentityError error)
    {

        return error.Code switch
        {
            "PasswordTooShort" => ValidationMessages.PasswordTooShort,
            "PasswordTooLong" => ValidationMessages.PasswordTooLong,
            _ => error.Description
        };
    }
}
