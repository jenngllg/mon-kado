using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Abstractions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

/// <summary>Accepts technical identifiers, bounded counters and predefined classifications only.</summary>
/// <param name="errorCodes">The host's compiled error-code allowlist.</param>
public class LogPropertyFilter(IEnumerable<string> errorCodes) : ILogPropertyFilter
{
    private static readonly HashSet<string> _identifiers =
    [
        "AdministratorId",
        "EventId",
        "ExportId",
        "ImageId",
        "MemberId",
        "MessageId",
        "OperationId",
        "OutboxMessageId",
        "OwnerId",
        "ParticipantId",
        "ReportId",
        "RequestId",
        "ReservationId",
        "ShareLinkId",
        "UserId",
        "WishId",
        "WishlistId"
    ];
    private static readonly HashSet<string> _counts =
    [
        "Count",
        "DeletedAccountCount",
        "DeletedEmailCount",
        "DeletedRequestCount",
        "DeletedSessionCount",
        "TotalCount",
        "StatusCode"
    ];
    private static readonly HashSet<string> _classifications =
    [
        "TOO_LARGE",
        "LEASE_LOST",
        "STORAGE_UNAVAILABLE",
        "DEPENDENCY_UNAVAILABLE",
        "ATTEMPT_CANCELLED",
        "MEMBER_UNAVAILABLE",
        "GENERATION_FAILED",
        "FAILED",
        "CANCELLED",
        "UNAVAILABLE"
    ];
    private readonly HashSet<string> _errorCodes = errorCodes.ToHashSet(StringComparer.Ordinal);
    private static readonly HashSet<string> _methods =
    [
        "GET",
        "POST",
        "PUT",
        "PATCH",
        "DELETE",
        "HEAD",
        "OPTIONS",
        "Other"
    ];

    /// <inheritdoc />
    public object? Filter(
        string name,
        object? value)
    {

        if (_identifiers.Contains(name))
            return value is Guid identifier ? identifier : null;

        if (_counts.Contains(name))
            return FilterCount(value);

        if (name is "Abandoned" or "IsReferenced")
            return value is bool ? value : null;

        if (name == "ElapsedMilliseconds")
            return FilterDuration(value);

        if (name is "Category" or "Failure" or "FailureCategory")
            return FilterEnumeration(value);

        return value is string text ? FilterText(
            name,
            text) : null;
    }

    /// <summary>Accepts only nonnegative integral counters.</summary>
    /// <param name="value">The untrusted structured value.</param>
    /// <returns>The counter, or null for an unsupported value.</returns>
    private static object? FilterCount(object? value)
    {

        return value is int count && count >= 0 || value is long total && total >= 0 ? value : null;
    }

    /// <summary>Accepts finite, nonnegative elapsed durations.</summary>
    /// <param name="value">The untrusted structured value.</param>
    /// <returns>The duration, or null for an unsupported value.</returns>
    private static double? FilterDuration(object? value)
    {

        return value is double number && double.IsFinite(number) && number >= 0 ? number : null;
    }

    /// <summary>Accepts defined classifications from the application assembly only.</summary>
    /// <param name="value">The untrusted structured value.</param>
    /// <returns>The compiled classification, or null for an unsupported value.</returns>
    private static string? FilterEnumeration(object? value)
    {

        return value is Enum enumeration && enumeration.GetType().Assembly == typeof(WorkerOperation).Assembly
            && Enum.IsDefined(
                enumeration.GetType(),
                enumeration) ? enumeration.ToString() : null;
    }

    /// <summary>Filters text through fixed vocabularies and registered route-template syntax.</summary>
    /// <param name="name">The structured property name.</param>
    /// <param name="text">The untrusted structured text.</param>
    /// <returns>The allowed text, or null for an unsupported value.</returns>
    private string? FilterText(
        string name,
        string text)
    {

        if (name == "ErrorCode")
            return _errorCodes.Contains(text) ? text : null;

        if (name == "Classification")
            return _classifications.Contains(text) ? text : null;

        if (name == "Method")
            return _methods.Contains(text) ? text : null;

        // Only registered route templates reach the application's HTTP completion event.
        if (name == "RoutePattern" && text.Length <= 200 && text.All(character => char.IsAsciiLetterOrDigit(character) || "/{}:-_".Contains(character)))
            return text;

        return null;
    }
}
