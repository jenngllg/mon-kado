using JennGllg.Fr.MonKado.Back.Application.Common.Constants;

using System.Reflection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Logging;

/// <summary>Resolves stable event names exclusively from the application's public constant catalog.</summary>
public static class LogEventCatalog
{
    private static readonly IReadOnlyDictionary<int, string> _names = typeof(LogEventIds)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .ToDictionary(
            field => (int)field.GetRawConstantValue()!,
            field => field.Name);

    /// <summary>Looks up a compiled event without trusting a caller-supplied event name.</summary>
    /// <param name="eventId">The numeric event.</param>
    /// <returns>The compiled name, or null for an external event.</returns>
    public static string? Find(int eventId)
    {

        return _names.GetValueOrDefault(eventId);
    }
}
