using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

using System.Net;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Services;

public class UrlImportClientTests
{
    [Fact]
    public async Task DownloadAsync_WhenRedirectCannotBeResolved_ReportsRemoteFailure()
    {
        // Arrange
        using var handler = new RecordingImportHttpHandler((
                _,
                _) => new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri(
                        "//",
                        UriKind.Relative) }
                });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var action = () => client.DownloadAsync(
            new Uri("https://example.com/"),
            1024,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(action);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DownloadAsync_WhenEmptyStreamHasNoContentType_ReturnsEmptyDocument()
    {
        // Arrange
        using var handler = new RecordingImportHttpHandler((
                _,
                _) => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new MemoryStream()) });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var document = await client.DownloadAsync(
            new Uri("https://example.com/"),
            10,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(document.Content);
        Assert.Null(document.MediaType);
        Assert.Null(document.Charset);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DownloadAsync_WhenRedirectIsRelative_ReturnsBoundedFinalDocument()
    {
        // Arrange
        using var handler = new RecordingImportHttpHandler((
                request,
                token) =>
            {
                Assert.True(token.CanBeCanceled);

                if (request.RequestUri?.AbsolutePath == "/start")
                {

                    return new HttpResponseMessage(HttpStatusCode.Redirect)
                    {
                        Headers =
                        {
                            Location = new Uri(
                                "/final",
                                UriKind.Relative)
                        }
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "hello",
                        Encoding.UTF8,
                        "text/html")
                };
            });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var document = await client.DownloadAsync(
            new Uri("https://example.com/start"),
            5,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "https://example.com/final",
            document.Url.AbsoluteUri);
        Assert.Equal(
            "hello",
            Encoding.UTF8.GetString(document.Content));
        Assert.Equal(
            "text/html",
            document.MediaType);
        Assert.Equal(
            "utf-8",
            document.Charset);
        Assert.Equal(
            2,
            handler.Requests.Count);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("http://example.com:8080/")]
    public async Task DownloadAsync_WhenRedirectHasForbiddenSyntax_DoesNotFollowIt(string target)
    {
        // Arrange
        using var handler = new RecordingImportHttpHandler((
                _,
                _) => new HttpResponseMessage(HttpStatusCode.Redirect) { Headers = { Location = new Uri(target) } });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var action = () => client.DownloadAsync(
            new Uri("https://example.com/"),
            10,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishImportUrlRejectedException>(action);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task DownloadAsync_WhenRedirectsLoop_StopsAfterConfiguredLimit(int status)
    {
        // Arrange
        using var handler = new RecordingImportHttpHandler((
                _,
                _) => new HttpResponseMessage((HttpStatusCode)status)
                {
                    Headers = { Location = new Uri(
                        "/again",
                        UriKind.Relative) }
                });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var action = () => client.DownloadAsync(
            new Uri("https://example.com/"),
            10,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(action);
        Assert.Equal(
            4,
            handler.Requests.Count);
    }

    [Theory]
    [InlineData(302)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task DownloadAsync_WhenResponseIsUnusable_DoesNotRetry(int status)
    {
        // Arrange
        using var handler = new RecordingImportHttpHandler((
                _,
                _) => new HttpResponseMessage((HttpStatusCode)status));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var action = () => client.DownloadAsync(
            new Uri("https://example.com/"),
            10,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(action);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DownloadAsync_WhenResponseExceedsLimit_RejectsBeforeBufferingWholeBody(bool declaredLength)
    {
        // Arrange
        using var handler = new RecordingImportHttpHandler((
                _,
                _) =>
            {
                var content = new ByteArrayContent(new byte[20]);
                content.Headers.ContentLength = declaredLength ? 20 : 0;

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content
                };
            });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var action = () => client.DownloadAsync(
            new Uri("https://example.com/"),
            10,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<HttpRequestException>(action);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DownloadAsync_WhenTransportRejectsDns_PreservesSafeRejection()
    {
        // Arrange
        using var handler = new RecordingImportHttpHandler((
                _,
                _) => throw new HttpRequestException(
                "wrapped",
                new WishImportUrlRejectedException()));
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var action = () => client.DownloadAsync(
            new Uri("https://example.com/"),
            10,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishImportUrlRejectedException>(action);
        Assert.Single(handler.Requests);
    }

    private static UrlImportClient CreateClient(HttpClient httpClient)
    {

        return new UrlImportClient(
            httpClient,
            Microsoft.Extensions.Options.Options.Create(new UrlImportOptions()));
    }
}
