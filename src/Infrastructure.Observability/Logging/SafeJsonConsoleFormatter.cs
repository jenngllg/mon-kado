using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Logging;

/// <summary>Writes one JSON line without rendering raw messages, arbitrary state or exception messages.</summary>
/// <param name="propertyFilter">The typed technical property allowlist.</param>
/// <param name="timeProvider">The UTC log clock.</param>
/// <param name="options">The validated service identity and release revision.</param>
public class SafeJsonConsoleFormatter(
    ILogPropertyFilter propertyFilter,
    TimeProvider timeProvider,
    IOptions<ObservabilityOptions> options) : ConsoleFormatter(FormatterName)
{
    /// <summary>Identifies the mandatory safe production formatter.</summary>
    public const string FormatterName = "monkado-safe-json";
    private const string ApplicationPrefix = "JennGllg.Fr.MonKado.Back.";
    private readonly string _startupCorrelationId = Guid.CreateVersion7().ToString("D");
    private readonly string _startupTraceId = ActivityTraceId.CreateRandom().ToString();

    /// <inheritdoc />
    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {

        if (logEntry.LogLevel == LogLevel.None)
            return;
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        var identifiers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CorrelationId"] = _startupCorrelationId,
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? _startupTraceId
        };
        scopeProvider?.ForEachScope(
            static (scope, ids) => ReadScope(
                scope,
                ids),
            identifiers);
        var applicationEvent = logEntry.Category.StartsWith(
            ApplicationPrefix,
            StringComparison.Ordinal);
        var eventName = applicationEvent ? LogEventCatalog.Find(logEntry.EventId.Id) : null;

        if (eventName is not null && logEntry.State is IEnumerable<KeyValuePair<string, object?>> state)
        {
            foreach (var pair in state)
            {
                var safe = propertyFilter.Filter(
                    pair.Key,
                    pair.Value);

                if (safe is not null)
                    properties[pair.Key] = safe;
            }
        }
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("timestamp", timeProvider.GetUtcNow().UtcDateTime);
            writer.WriteString("service", options.Value.Service);
            writer.WriteString("version", options.Value.Version);
            writer.WriteString("level", logEntry.LogLevel.ToString());
            writer.WriteString("category", SafeCategory(logEntry.Category));
            writer.WriteNumber("eventId", logEntry.EventId.Id);
            writer.WriteString("eventName", eventName ?? "ExternalEvent");
            writer.WriteString("correlationId", identifiers["CorrelationId"]);
            writer.WriteString("traceId", identifiers["TraceId"]);
            writer.WritePropertyName("properties");
            JsonSerializer.Serialize(
                writer,
                properties);

            if (logEntry.Exception is { } exception)
            {
                writer.WriteString("exceptionType", exception.GetType().FullName);
                // CLR frames retain the technical call chain without paths, messages or Exception.Data.
                writer.WriteString("stackTrace", new StackTrace(exception, false).ToString());
            }
            writer.WriteEndObject();
        }
        textWriter.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>Copies only validated correlation values from structured scopes.</summary>
    /// <param name="scope">The potentially unstructured scope.</param>
    /// <param name="identifiers">The bounded output identifiers.</param>
    private static void ReadScope(
        object? scope,
        Dictionary<string, string> identifiers)
    {

        if (scope is not IEnumerable<KeyValuePair<string, object?>> properties)
            return;
        foreach (var pair in properties)
        {

            if (pair.Value is not string value)
                continue;

            if (pair.Key == "CorrelationId" && Guid.TryParse(value, out var id) && id != Guid.Empty)
                identifiers[pair.Key] = value;

            if (pair.Key == "TraceId" && value.Length == 32 && value.All(char.IsAsciiHexDigit) && value.Any(character => character != '0'))
                identifiers[pair.Key] = value;
        }
    }

    /// <summary>Retains static application/framework categories without allowing arbitrary external labels.</summary>
    /// <param name="category">The provider category.</param>
    /// <returns>The safe category or a fixed external marker.</returns>
    private static string SafeCategory(string category)
    {

        return (category.StartsWith(ApplicationPrefix, StringComparison.Ordinal)
                || category.StartsWith("Microsoft.", StringComparison.Ordinal)
                || category.StartsWith("System.", StringComparison.Ordinal))
            && category.Length <= 200 && category.All(character => char.IsAsciiLetterOrDigit(character) || character == '.')
            ? category
            : "External";
    }
}
