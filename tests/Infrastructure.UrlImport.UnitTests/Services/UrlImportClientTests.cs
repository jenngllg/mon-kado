using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

using System.Net;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Services;

public class UrlImportClientTests
{
    [Theory]
    [InlineData(2097153, true)]
    [InlineData(4194304, true)]
    [InlineData(4194305, false)]
    public async Task DownloadAsync_WhenDocumentExceedsTwoMebibytes_RespectsConfiguredFourMebibyteCap(
        int contentLength,
        bool expectedSuccess)
    {
        // Arrange
        using var handler = new RecordingImportHttpHandler((
                _,
                _) => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[contentLength])
                });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);
        var cancellationToken = TestContext.Current.CancellationToken;

        // Act
        var action = () => client.DownloadAsync(
            new Uri("https://example.com/product"),
            new UrlImportOptions().MaximumHtmlBytes,
            cancellationToken);

        // Assert

        if (!expectedSuccess)
        {
            await Assert.ThrowsAsync<HttpRequestException>(action);

            return;
        }
        var document = await action();
        Assert.Equal(
            contentLength,
            document.Content.Length);
        Assert.Single(handler.Requests);
    }
    [Fact]
    public async Task DownloadAsync_WhenClientNegotiatesHttp2_PreservesVersionPolicyAcrossRedirects()
    {
        // Arrange
        var policies = new List<(Version Version, HttpVersionPolicy Policy)>();
        using var handler = new RecordingImportHttpHandler((
                request,
                _) =>
            {
                policies.Add((request.Version, request.VersionPolicy));

                return request.RequestUri?.AbsolutePath == "/start"
                ? new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri(
                        "/product",
                        UriKind.Relative) }
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("product")
                };
            });
        using var httpClient = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
        var client = CreateClient(httpClient);

        // Act
        await client.DownloadAsync(
            new Uri("https://example.com/start"),
            1024,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            2,
            handler.Requests.Count);
        Assert.All(
            policies,
            policy =>
            {
                Assert.Equal(
                    HttpVersion.Version20,
                    policy.Version);
                Assert.Equal(
                    HttpVersionPolicy.RequestVersionOrLower,
                    policy.Policy);
            });
    }

    [Theory]
    [InlineData("https://amzn.eu/d/Example")]
    [InlineData("https://amzn.to/Example")]
    public async Task DownloadAsync_WhenAmazonShortLinkRedirects_PreservesFinalProductUrl(string shortUrl)
    {
        // Arrange
        var productUrl = new Uri("https://www.amazon.fr/Example/dp/B0EXAMPLE01?ref=sharing");
        using var handler = new RecordingImportHttpHandler((
                request,
                token) =>
            {
                Assert.True(token.CanBeCanceled);

                if (request.RequestUri?.AbsoluteUri == shortUrl)
                    return new HttpResponseMessage(HttpStatusCode.Redirect)
                    {
                        Headers = { Location = productUrl }
                    };

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "<span id='productTitle'>Book</span>",
                        Encoding.UTF8,
                        "text/html")
                };
            });
        using var httpClient = new HttpClient(handler);
        var client = CreateClient(httpClient);

        // Act
        var document = await client.DownloadAsync(
            new Uri(shortUrl),
            1024,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            productUrl,
            document.Url);
        Assert.Equal(
            2,
            handler.Requests.Count);
        Assert.Equal(
            "<span id='productTitle'>Book</span>",
            Encoding.UTF8.GetString(document.Content));
    }

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
