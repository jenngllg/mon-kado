using FluentValidation;

using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using System.Buffers;
using System.Globalization;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>
/// Defines the shared validation rules for member display names.
/// </summary>
public static class DisplayNameValidationExtensions
{
    private const int MaximumDisplayNameLength = 80;

    /// <summary>
    /// Applies all display name validation rules.
    /// </summary>
    /// <typeparam name="T">The type being validated.</typeparam>
    /// <param name="ruleBuilder">The display name rule builder.</param>
    /// <returns>The configured rule builder.</returns>
    public static IRuleBuilderOptions<T, string?> ApplyDisplayNameRules<T>(
        this IRuleBuilderInitial<T, string?> ruleBuilder)
    {

        return ruleBuilder
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(value => IsNotBlank(value!))
            .WithMessage(ValidationMessages.MandatoryProperty)
            .Must(value => IsWellFormed(value!))
            .WithMessage("The display name must contain valid Unicode characters.")
            .Must(value => IsWithinMaximumLength(value!))
            .WithMessage($"The display name must not exceed {MaximumDisplayNameLength} characters.")
            .Must(value => DoesNotContainControlCharacters(value!))
            .WithMessage("The display name must not contain control characters.");
    }

    private static bool IsNotBlank(string value)
    {

        return value.Trim().Length > 0;
    }

    private static bool IsWellFormed(string value)
    {
        var remaining = value.AsSpan();

        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(
                remaining,
                out _,
                out var charactersConsumed);

            if (status is not OperationStatus.Done)
                return false;

            remaining = remaining[charactersConsumed..];
        }

        return true;
    }

    private static bool IsWithinMaximumLength(string value)
    {

        return value.Trim().EnumerateRunes().Count() <= MaximumDisplayNameLength;
    }

    private static bool DoesNotContainControlCharacters(string value)
    {

        return value.EnumerateRunes().All(rune =>
            Rune.GetUnicodeCategory(rune) is not UnicodeCategory.Control);
    }
}
