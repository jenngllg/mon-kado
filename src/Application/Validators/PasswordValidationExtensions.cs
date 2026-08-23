using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Provides reusable password validation rules.
/// </summary>
public static class PasswordValidationExtensions
{
    private const int MinimumPasswordLength = 12;
    private const int MaximumPasswordLength = 128;

    /// <summary>
    /// Applies rules for a password submitted as an existing credential.
    /// </summary>
    /// <typeparam name="T">The validated model type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <returns>The configured rule builder.</returns>
    public static IRuleBuilderOptions<T, string?> ApplySubmittedPasswordRules<T>(
        this IRuleBuilderInitial<T, string?> ruleBuilder)
    {

        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(IsWithinMaximumLength)
            .WithMessage(ValidationMessages.PasswordTooLong);
    }

    /// <summary>
    /// Applies the complete policy for a newly selected password.
    /// </summary>
    /// <typeparam name="T">The validated model type.</typeparam>
    /// <param name="ruleBuilder">The rule builder.</param>
    /// <returns>The configured rule builder.</returns>
    public static IRuleBuilderOptions<T, string?> ApplyNewPasswordRules<T>(
        this IRuleBuilderInitial<T, string?> ruleBuilder)
    {

        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(IsWithinMinimumLength)
            .WithMessage(ValidationMessages.PasswordTooShort)
            .Must(IsWithinMaximumLength)
            .WithMessage(ValidationMessages.PasswordTooLong);
    }

    private static bool IsWithinMinimumLength(string? password)
    {
        ArgumentNullException.ThrowIfNull(password);

        return password.EnumerateRunes().Count() >= MinimumPasswordLength;
    }

    private static bool IsWithinMaximumLength(string? password)
    {
        ArgumentNullException.ThrowIfNull(password);

        return password.EnumerateRunes().Count() <= MaximumPasswordLength;
    }
}
