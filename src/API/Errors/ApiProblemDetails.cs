using System.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Api.Errors;

internal static class ApiProblemDetails
{
    private const string ProblemTypeBaseUri = "https://api.mon-kado.fr/problems";

    public static IResult Create(
        HttpContext context,
        int statusCode,
        string slug,
        string title,
        string detail,
        string code,
        IReadOnlyDictionary<string, string[]>? errors = null)
    {
        Dictionary<string, object?> extensions = new(StringComparer.Ordinal)
        {
            ["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier,
            ["code"] = code
        };

        if (errors is not null)
        {
            extensions["errors"] = errors;
        }

        return Results.Problem(
            detail: detail,
            instance: context.Request.Path,
            statusCode: statusCode,
            title: title,
            type: $"{ProblemTypeBaseUri}/{slug}",
            extensions: extensions);
    }
}
