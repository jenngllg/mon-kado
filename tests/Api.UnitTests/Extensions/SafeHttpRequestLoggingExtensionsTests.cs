using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Extensions;

public class SafeHttpRequestLoggingExtensionsTests
{
    private readonly Mock<IApplicationTelemetry> _telemetryMock = new(MockBehavior.Strict);
    private readonly Mock<TimeProvider> _timeProviderMock = new(MockBehavior.Strict);

    [Theory]
    [InlineData(false, false, 429)]
    [InlineData(true, false, 500)]
    [InlineData(true, true, 500)]
    public async Task Request_WhenCompletedOrInterrupted_RecordsDurationInFinally(
        bool throws,
        bool abandoned,
        int expectedStatus)
    {
        // Arrange
        _timeProviderMock.SetupSequence(provider => provider.GetTimestamp())
            .Returns(0)
            .Returns(3000);
        _timeProviderMock.SetupGet(provider => provider.TimestampFrequency)
            .Returns(1000);
        _telemetryMock.Setup(telemetry => telemetry.RecordHttp(
            expectedStatus,
            TimeSpan.FromSeconds(3),
            abandoned));
        var registrations = new ServiceCollection();
        registrations.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        registrations.AddSingleton(_timeProviderMock.Object);
        registrations.AddSingleton(_telemetryMock.Object);
        using var services = registrations.BuildServiceProvider();
        var application = new ApplicationBuilder(services);
        application.UseSafeHttpRequestLogging();
        application.Run(context =>
        {
            context.Response.StatusCode = 429;

            if (throws)
                throw new InvalidOperationException("test failure");

            return Task.CompletedTask;
        });
        var pipeline = application.Build();
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            RequestAborted = new CancellationToken(abandoned)
        };
        context.Request.Method = "GET";

        // Act
        var exception = await Record.ExceptionAsync(() => pipeline(context));

        // Assert
        Assert.Equal(throws, exception is not null);
        _telemetryMock.Verify(telemetry => telemetry.RecordHttp(
            expectedStatus,
            TimeSpan.FromSeconds(3),
            abandoned), Times.Once);
        _timeProviderMock.Verify(provider => provider.GetTimestamp(), Times.Exactly(2));
        _timeProviderMock.VerifyGet(provider => provider.TimestampFrequency, Times.Once);
        _telemetryMock.VerifyNoOtherCalls();
        _timeProviderMock.VerifyNoOtherCalls();
    }
}
