using JennGllg.Fr.MonKado.Back.Api.Results;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests.Results;

public class PersonalDataExportFileResultTests
{
    private readonly Mock<IActionResultExecutor<FileStreamResult>> _executorMock;
    private readonly Mock<IHttpRequestLifetimeFeature> _lifetimeMock;
    public PersonalDataExportFileResultTests()
    {
        _executorMock = new(MockBehavior.Strict);
        _lifetimeMock = new(MockBehavior.Strict);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ExecuteResultAsync_WhenStorageFails_SanitizesBeforeHeadersOrAbortsStartedResponse(
        bool started,
        bool unauthorized)
    {
        // Arrange
        using var stream = new MemoryStream();
        var id = Guid.CreateVersion7();
        var result = new PersonalDataExportFileResult(new PersonalDataExportDownload
        {
            ExportId = id,
            Content = stream
        });
        var logger = new RecordingExceptionLogger<PersonalDataExportFileResult>();
        await using var services = new ServiceCollection()
            .AddSingleton(_executorMock.Object)
            .AddSingleton<ILogger<PersonalDataExportFileResult>>(logger)
            .BuildServiceProvider();
        var http = new DefaultHttpContext
        {
            RequestServices = services
        };
        http.Features.Set<IHttpResponseFeature>(new StartedExportResponseFeature(started));
        http.Features.Set(_lifetimeMock.Object);
        var context = new ActionContext(
            http,
            new RouteData(),
            new ActionDescriptor());
        var exception = unauthorized ? (Exception)new UnauthorizedAccessException("/private/member/export.zip") : new IOException("/private/member/export.zip");
        _executorMock
            .Setup(executor => executor.ExecuteAsync(
                context,
                result))
            .ThrowsAsync(exception);

        if (started)
            _lifetimeMock.Setup(lifetime => lifetime.Abort());

        // Act
        if (started)
            await result.ExecuteResultAsync(context);
        else
        {
            var failure = await Assert.ThrowsAsync<PersonalDataExportStorageUnavailableException>(() => result.ExecuteResultAsync(context));
            Assert.Null(failure.InnerException);
            Assert.DoesNotContain(
                "/private/",
                failure.ToString());
        }

        // Assert
        Assert.Equal(
            "application/zip",
            result.ContentType);
        Assert.Equal(
            $"monkado-data-{id:D}.zip",
            result.FileDownloadName);
        Assert.All(
            logger.Entries,
            entry => Assert.DoesNotContain(
                "/private/",
                entry));
        Assert.Equal(
            started ? 1 : 0,
            logger.Entries.Count);
        _executorMock.Verify(
            executor => executor.ExecuteAsync(
                context,
                result),
            Times.Once);

        if (started)
            _lifetimeMock.Verify(
                lifetime => lifetime.Abort(),
                Times.Once);
        _executorMock.VerifyNoOtherCalls();
        _lifetimeMock.VerifyNoOtherCalls();
    }
}
