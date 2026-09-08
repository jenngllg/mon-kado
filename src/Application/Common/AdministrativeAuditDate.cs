using System.Globalization;

namespace JennGllg.Fr.MonKado.Back.Application.Common;

/// <summary>Parses explicitly zoned ISO 8601 audit boundaries independently of machine culture and timezone.</summary>
public static class AdministrativeAuditDate
{
    private static readonly string[] _formats =
    [
        "yyyy-MM-dd'T'HH:mm:ss'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.f'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.ff'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.ffff'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.fffff'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'",
        "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
        "yyyy-MM-dd'T'HH:mm:sszzz",
        "yyyy-MM-dd'T'HH:mm:ss.fzzz",
        "yyyy-MM-dd'T'HH:mm:ss.ffzzz",
        "yyyy-MM-dd'T'HH:mm:ss.fffzzz",
        "yyyy-MM-dd'T'HH:mm:ss.ffffzzz",
        "yyyy-MM-dd'T'HH:mm:ss.fffffzzz",
        "yyyy-MM-dd'T'HH:mm:ss.ffffffzzz",
        "yyyy-MM-dd'T'HH:mm:ss.fffffffzzz"
    ];

    /// <summary>Checks that a boundary includes a time and an explicit timezone.</summary>
    /// <param name="value">The supplied boundary.</param>
    /// <returns>Whether the boundary follows the supported ISO formats.</returns>
    public static bool IsValid(string value)
    {

        return DateTimeOffset.TryParseExact(
            value,
            _formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out _);
    }

    /// <summary>Normalizes a centrally validated boundary to UTC.</summary>
    /// <param name="value">The validated boundary.</param>
    /// <returns>The UTC date.</returns>
    /// <exception cref="FormatException">The value was not validated before normalization.</exception>
    public static DateTime Parse(string value)
    {

        return DateTimeOffset.ParseExact(
            value,
            _formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal)
            .UtcDateTime;
    }
}
