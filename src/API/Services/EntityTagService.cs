using JennGllg.Fr.MonKado.Back.Api.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Common.Models;

using System.Globalization;

namespace JennGllg.Fr.MonKado.Back.Api.Services;

/// <summary>
/// Formats and validates strong resource entity tags.
/// </summary>
public class EntityTagService : IEntityTagService
{
    private const int EncodedVersionLength = 8;

    /// <inheritdoc />
    public string Format(uint version)
    {

        return $"\"{version:x8}\"";
    }

    /// <inheritdoc />
    public uint Parse(string? value)
    {

        if (value is null)
            throw new PreconditionRequiredException();

        if (value.Length != EncodedVersionLength + 2 ||
            value[0] != '"' ||
            value[^1] != '"' ||
            !uint.TryParse(
                value.AsSpan(
                    1,
                    EncodedVersionLength),
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var version))
        {

            throw CreateValidationException();
        }

        if (!string.Equals(
            value,
            Format(version),
            StringComparison.Ordinal))
        {

            throw CreateValidationException();
        }

        return version;
    }

    /// <inheritdoc />
    public uint? ParseOptional(string? value)
    {
        if (value is null)
            return null;

        return Parse(value);
    }

    private static RequestValidationException CreateValidationException()
    {

        return new RequestValidationException(
        [
            new ValidationError(
                "ifMatch",
                "The If-Match header must contain one valid strong entity tag.")
        ]);
    }
}
