using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Logging;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Observability.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using System.Diagnostics;
using System.Text.Json;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Observability.UnitTests.Logging;

public class SafeJsonConsoleFormatterTests
{
    private const string Category = "JennGllg.Fr.MonKado.Back.Api.Logging.ApiLogMessages";
    private readonly ControlledTimeProvider _clock = new();
    private readonly SafeJsonConsoleFormatter _formatter;

    public SafeJsonConsoleFormatterTests()
    {
        _formatter = new SafeJsonConsoleFormatter(
            new LogPropertyFilter(["ACCOUNT_NOT_FOUND"]),
            _clock,
            Microsoft.Extensions.Options.Options.Create(new ObservabilityOptions()));
    }

    [Fact]
    public void Write_WhenSensitiveStateAndException_OnlyEmitsSafeValues()
    {
        // Arrange
        var state = new Dictionary<string, object?>
        {
            ["{OriginalFormat}"] = "PRIVATE_MESSAGE",
            ["Email"] = "PRIVATE_EMAIL@example.test",
            ["Password"] = "PRIVATE_PASSWORD",
            ["Token"] = "PRIVATE_TOKEN",
            ["Body"] = "PRIVATE_BODY",
            ["StatusCode"] = 503,
            ["Method"] = "GET"
        };
        var entry = new LogEntry<Dictionary<string, object?>>(
            LogLevel.Error,
            Category,
            new EventId(LogEventIds.HttpRequestCompleted, "PRIVATE_EVENT_NAME"),
            state,
            new InvalidOperationException("PRIVATE_EXCEPTION"),
            (_, _) => throw new InvalidOperationException("Formatter must never be invoked"));
        using var output = new StringWriter();

        // Act
        _formatter.Write(
            entry,
            null,
            output);

        // Assert
        var text = output.ToString();
        Assert.DoesNotContain("PRIVATE", text);
        using var json = JsonDocument.Parse(text);
        Assert.Equal("HttpRequestCompleted", json.RootElement.GetProperty("eventName").GetString());
        Assert.Equal("api", json.RootElement.GetProperty("service").GetString());
        Assert.Equal("local", json.RootElement.GetProperty("version").GetString());
        Assert.Equal(_clock.GetUtcNow().UtcDateTime, json.RootElement.GetProperty("timestamp").GetDateTime());
        Assert.Equal(503, json.RootElement.GetProperty("properties").GetProperty("StatusCode").GetInt32());
        Assert.Equal(typeof(InvalidOperationException).FullName, json.RootElement.GetProperty("exceptionType").GetString());
        Assert.Equal(32, json.RootElement.GetProperty("traceId").GetString()!.Length);
        Assert.True(Guid.TryParse(json.RootElement.GetProperty("correlationId").GetString(), out _));
        Assert.Single(text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Write_WhenScopeContainsValues_OnlyKeepsValidCorrelation(bool valid)
    {
        // Arrange
        var correlation = Guid.CreateVersion7().ToString("D");
        var trace = new string('a', 32);
        var scopes = new LoggerExternalScopeProvider();
        using var ignoredScope = scopes.Push("PRIVATE_SCOPE");
        using var scope = scopes.Push(new Dictionary<string, object?>
        {
            ["CorrelationId"] = valid ? correlation : "PRIVATE_CORRELATION",
            ["TraceId"] = valid ? trace : "PRIVATE_TRACE",
            ["RequestId"] = 123,
            ["Email"] = "PRIVATE_EMAIL"
        });
        var entry = new LogEntry<string>(
            LogLevel.Information,
            Category,
            new EventId(LogEventIds.HttpRequestCompleted),
            "PRIVATE_STATE",
            null,
            (_, _) => "PRIVATE_FORMAT");
        using var output = new StringWriter();

        // Act
        _formatter.Write(
            entry,
            scopes,
            output);

        // Assert
        Assert.DoesNotContain("PRIVATE", output.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(valid, json.RootElement.GetProperty("correlationId").GetString() == correlation);
        Assert.Equal(valid, json.RootElement.GetProperty("traceId").GetString() == trace);
    }

    [Theory]
    [InlineData("Microsoft.Hosting.Lifetime")]
    [InlineData("System.Net.Http")]
    [InlineData("PRIVATE/category")]
    [InlineData("Microsoft.PRIVATE/category")]
    public void Write_WhenExternalEvent_DropsMessageAndState(string category)
    {
        // Arrange
        var entry = new LogEntry<string>(
            LogLevel.Warning,
            category,
            new EventId(123),
            "PRIVATE_STATE",
            null,
            (_, _) => "PRIVATE_FORMAT");
        using var output = new StringWriter();

        // Act
        _formatter.Write(
            entry,
            null,
            output);

        // Assert
        Assert.DoesNotContain("PRIVATE", output.ToString());
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal("ExternalEvent", json.RootElement.GetProperty("eventName").GetString());
    }

    [Fact]
    public void Write_WhenNone_DoesNotProduceLine()
    {
        // Arrange
        var entry = new LogEntry<string>(LogLevel.None, Category, default, "", null, (_, _) => "");
        using var output = new StringWriter();

        // Act
        _formatter.Write(
            entry,
            null,
            output);

        // Assert
        Assert.Empty(output.ToString());
        Assert.Null(LogEventCatalog.Find(-1));
    }

    [Theory]
    [InlineData('0')]
    [InlineData('z')]
    public void Write_WhenScopeHasInvalidTrace_PreservesCurrentActivity(char character)
    {
        // Arrange
        using var activity = new Activity("UnitTest")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();
        var scopes = new LoggerExternalScopeProvider();
        using var scope = scopes.Push(new Dictionary<string, object?>
        {
            ["TraceId"] = new string(
                character,
                32),
            ["CorrelationId"] = Guid.Empty.ToString("D")
        });
        var entry = new LogEntry<string>(
            LogLevel.Information,
            Category,
            default,
            "",
            null,
            (_, _) => "");
        using var output = new StringWriter();

        // Act
        _formatter.Write(
            entry,
            scopes,
            output);

        // Assert
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(
            activity.TraceId.ToString(),
            json.RootElement.GetProperty("traceId").GetString());
        Assert.NotEqual(
            Guid.Empty.ToString("D"),
            json.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public void Write_WhenCategoryIsUnbounded_ReplacesCategoryWithFixedMarker()
    {
        // Arrange
        var entry = new LogEntry<string>(
            LogLevel.Information,
            "System." + new string(
                'x',
                201),
            default,
            "",
            null,
            (_, _) => "");
        using var output = new StringWriter();

        // Act
        _formatter.Write(
            entry,
            null,
            output);

        // Assert
        using var json = JsonDocument.Parse(output.ToString());
        Assert.Equal(
            "External",
            json.RootElement.GetProperty("category").GetString());
    }
}
