using JennGllg.Fr.MonKado.Back.Worker.Logging;

using System.Diagnostics;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests.Logging;

public class WorkerLogScopeTests
{
    [Fact]
    public void Begin_WhenOperationStarts_CreatesCorrelationAndTraceScope()
    {
        // Arrange
        const string operation = "AuthenticationEmailDelivery";
        var logger = new RecordingLogger<WorkerLogScopeTests>();

        // Act
        using var operationScope = WorkerLogScope.Begin(
            logger,
            operation);
        var activity = Activity.Current;

        // Assert
        var scope = Assert.Single(logger.Scopes);
        var correlationId = Assert.IsType<string>(scope["CorrelationId"]);
        Assert.True(Guid.TryParse(correlationId, out _));
        Assert.Equal(
            activity?.TraceId.ToString(),
            scope["TraceId"]);
        Assert.Equal(
            operation,
            scope["Operation"]);
        Assert.Equal(
            ActivityIdFormat.W3C,
            activity?.IdFormat);
    }

    [Fact]
    public void Dispose_WhenOperationEnds_RestoresPreviousActivity()
    {
        // Arrange
        var logger = new RecordingLogger<WorkerLogScopeTests>();
        using var parentActivity = new Activity("Parent")
            .Start();
        var operationScope = WorkerLogScope.Begin(
            logger,
            "AuthenticationEmailDelivery");

        // Act
        operationScope.Dispose();

        // Assert
        Assert.Same(
            parentActivity,
            Activity.Current);
    }

    [Fact]
    public void Dispose_WhenLoggerDoesNotCreateScope_StopsActivity()
    {
        // Arrange
        var previousActivity = Activity.Current;
        var activity = new Activity("AuthenticationEmailDelivery")
            .Start();
        var operationScope = new WorkerOperationScope(
            activity,
            null);

        // Act
        operationScope.Dispose();

        // Assert
        Assert.Same(
            previousActivity,
            Activity.Current);
    }
}
