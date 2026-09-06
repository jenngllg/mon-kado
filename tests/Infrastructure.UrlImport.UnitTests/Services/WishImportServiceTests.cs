using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

using Moq;

using System.Text;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests.Services;

public class WishImportServiceTests
{
    [Theory]
    [InlineData("TEXT/HTML")]
    [InlineData("APPLICATION/XHTML+XML")]
    public async Task PreviewAsync_WhenHtmlMediaTypeUsesDifferentCase_ExtractsSuggestions(string mediaType)
    {
        // Arrange
        SetupPage(
            "<title>Gift</title>",
            mediaType);

        // Act
        var result = await _service.PreviewAsync(
            ProductUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "Gift",
            result.Name);
        Assert.DoesNotContain(
            WishImportWarnings.PageUnavailable,
            result.Warnings);
        VerifyPage();
        _clientMock.VerifyNoOtherCalls();
        _processorMock.VerifyNoOtherCalls();
    }

    private const string ProductUrl = "https://merchant.example/product";
    private const string ImageUrl = "https://merchant.example/image.png";
    private readonly Mock<IUrlImportClient> _clientMock;
    private readonly Mock<IGiftImageProcessor> _processorMock;
    private readonly ManualImportTimeProvider _timeProvider;
    private readonly WishImportService _service;
    public WishImportServiceTests()
    {
        _clientMock = new(MockBehavior.Strict);
        _processorMock = new(MockBehavior.Strict);
        _timeProvider = new();
        _service = new(
            _clientMock.Object,
            new MerchantMetadataExtractor(),
            _processorMock.Object,
            Microsoft.Extensions.Options.Options.Create(new UrlImportOptions()),
            _timeProvider);
    }

    [Fact]
    public async Task PreviewAsync_WhenMetadataAndImageAreValid_ReturnsSuggestionsWithSharedBudget()
    {
        // Arrange
        SetupPage("""
            <meta property="og:title" content="Gift">
            <meta property="og:image" content="/image.png">
            <meta property="product:price:amount" content="12.34">
            <meta property="product:price:currency" content="EUR">
        """);
        byte[] source = [
            1,
            2
        ];
        CancellationToken imageToken = default;
        _clientMock
            .Setup(client => client.DownloadAsync(
                new Uri(ImageUrl),
                GiftImageConstraints.MaximumInputLength,
                It.IsAny<CancellationToken>()))
            .Callback<Uri, int, CancellationToken>((
                _,
                _,
                token) => imageToken = token)
            .ReturnsAsync(new ImportDocument
            {
                Url = new Uri(ImageUrl),
                Content = source
            });
        _processorMock
            .Setup(processor => processor.ProcessAsync(
                It.Is<ReadOnlyMemory<byte>>(content => content
                        .ToArray()
                        .SequenceEqual(source)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessedGiftImage(
                new byte[] {
                    3,
                    4
                },
                new byte[32]));

        // Act
        var result = await _service.PreviewAsync(
            ProductUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "Gift",
            result.Name);
        Assert.Equal(
            12.34m,
            result.Price);
        Assert.NotNull(result.Image);
        Assert.Equal(
            "AwQ=",
            result.Image.ContentBase64);
        Assert.Equal(
            "image/webp",
            result.Image.ContentType);
        Assert.Empty(result.Warnings);
        Assert.Equal(
            TimeSpan.FromSeconds(20),
            _timeProvider.DueTime);
        VerifyPage(imageToken);
        _clientMock.Verify(
            client => client.DownloadAsync(
                new Uri(ImageUrl),
                GiftImageConstraints.MaximumInputLength,
                imageToken),
            Times.Once);
        _processorMock.Verify(
            processor => processor.ProcessAsync(
                It.Is<ReadOnlyMemory<byte>>(content => content
                        .ToArray()
                        .SequenceEqual(source)),
                imageToken),
            Times.Once);
        _clientMock.VerifyNoOtherCalls();
        _processorMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("network")]
    [InlineData("io")]
    [InlineData("timeout")]
    public async Task PreviewAsync_WhenPageCannotBeRead_OffersManualCompletion(string failure)
    {
        // Arrange
        _clientMock
            .Setup(client => client.DownloadAsync(
                new Uri(ProductUrl),
                2 * 1024 * 1024,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateFailure(failure));

        // Act
        var result = await _service.PreviewAsync(
            ProductUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            ProductUrl,
            result.Url);
        Assert.Null(result.Name);
        Assert.Null(result.Image);
        Assert.Contains(
            WishImportWarnings.PageUnavailable,
            result.Warnings);
        VerifyPage();
        _clientMock.VerifyNoOtherCalls();
        _processorMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("application/json")]
    [InlineData("image/png")]
    public async Task PreviewAsync_WhenPageIsNotHtml_OffersManualCompletion(string? mediaType)
    {
        // Arrange
        SetupPage(
            "<title>Not HTML</title>",
            mediaType);

        // Act
        var result = await _service.PreviewAsync(
            ProductUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Name);
        Assert.Contains(
            WishImportWarnings.PageUnavailable,
            result.Warnings);
        VerifyPage();
        _clientMock.VerifyNoOtherCalls();
        _processorMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("network")]
    [InlineData("io")]
    [InlineData("timeout")]
    [InlineData("rejected")]
    public async Task PreviewAsync_WhenImageCannotBeFetched_PreservesText(string failure)
    {
        // Arrange
        SetupPage("<title>Gift</title><meta property='og:image' content='/image.png'>");
        _clientMock
            .Setup(client => client.DownloadAsync(
                new Uri(ImageUrl),
                GiftImageConstraints.MaximumInputLength,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(CreateFailure(failure));

        // Act
        var result = await _service.PreviewAsync(
            ProductUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "Gift",
            result.Name);
        Assert.Null(result.Image);
        Assert.Contains(
            WishImportWarnings.ImageUnavailable,
            result.Warnings);
        VerifyPage();
        _clientMock.Verify(
            client => client.DownloadAsync(
                new Uri(ImageUrl),
                GiftImageConstraints.MaximumInputLength,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _clientMock.VerifyNoOtherCalls();
        _processorMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("invalid")]
    [InlineData("unsupported")]
    [InlineData("oversized")]
    public async Task PreviewAsync_WhenImageCannotBeNormalized_PreservesText(string failure)
    {
        // Arrange
        SetupPage("<title>Gift</title><meta property='og:image' content='/image.png'>");
        _clientMock
            .Setup(client => client.DownloadAsync(
                new Uri(ImageUrl),
                GiftImageConstraints.MaximumInputLength,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportDocument
            {
                Url = new Uri(ImageUrl),
                Content = []
            });
        var setup = _processorMock.Setup(processor => processor.ProcessAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()));

        if (failure == "oversized")
            setup.ReturnsAsync(new ProcessedGiftImage(
                    new byte[GiftImageConstraints.MaximumInputLength + 1],
                    new byte[32]));
        else
            setup.ThrowsAsync(CreateFailure(failure));

        // Act
        var result = await _service.PreviewAsync(
            ProductUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "Gift",
            result.Name);
        Assert.Null(result.Image);
        Assert.Contains(
            WishImportWarnings.ImageUnavailable,
            result.Warnings);
        VerifyPage();
        _clientMock.Verify(
            client => client.DownloadAsync(
                new Uri(ImageUrl),
                GiftImageConstraints.MaximumInputLength,
                It.IsAny<CancellationToken>()),
            Times.Once);
        _processorMock.Verify(
            processor => processor.ProcessAsync(
                It.IsAny<ReadOnlyMemory<byte>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _clientMock.VerifyNoOtherCalls();
        _processorMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PreviewAsync_WhenPriceIsForeign_ReturnsSpecificWarning()
    {
        // Arrange
        SetupPage(
            "<meta property='product:price:amount' content='42'><meta property='product:price:currency' content='USD'>",
            "application/xhtml+xml");

        // Act
        var result = await _service.PreviewAsync(
            ProductUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(result.Price);
        Assert.Contains(
            WishImportWarnings.CurrencyUnsupported,
            result.Warnings);
        VerifyPage();
        _clientMock.VerifyNoOtherCalls();
        _processorMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PreviewAsync_WhenTotalBudgetExpires_StopsWithoutRealWait()
    {
        // Arrange
        _clientMock
            .Setup(client => client.DownloadAsync(
                new Uri(ProductUrl),
                2 * 1024 * 1024,
                It.IsAny<CancellationToken>()))
            .Returns<Uri, int, CancellationToken>((
                _,
                _,
                token) =>
            {
                Assert.NotNull(_timeProvider.Timer);
                _timeProvider.Timer.Fire();
                Assert.True(token.IsCancellationRequested);

                return Task.FromCanceled<ImportDocument>(token);
            });

        // Act
        var result = await _service.PreviewAsync(
            ProductUrl,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains(
            WishImportWarnings.PageUnavailable,
            result.Warnings);
        VerifyPage();
        _clientMock.VerifyNoOtherCalls();
        _processorMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PreviewAsync_WhenCallerCancels_PropagatesCancellationToDownload()
    {
        // Arrange
        using var source = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _clientMock
            .Setup(client => client.DownloadAsync(
                new Uri(ProductUrl),
                2 * 1024 * 1024,
                It.IsAny<CancellationToken>()))
            .Returns<Uri, int, CancellationToken>((
                _,
                _,
                token) =>
            {
                source.Cancel();
                Assert.True(token.IsCancellationRequested);

                return Task.FromCanceled<ImportDocument>(token);
            });

        // Act
        var action = () => _service.PreviewAsync(
            ProductUrl,
            source.Token);

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
        VerifyPage();
        _clientMock.VerifyNoOtherCalls();
        _processorMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PreviewAsync_WhenDestinationIsRejected_DoesNotHideSecurityError()
    {
        // Arrange
        _clientMock
            .Setup(client => client.DownloadAsync(
                new Uri(ProductUrl),
                2 * 1024 * 1024,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new WishImportUrlRejectedException());

        // Act
        var action = () => _service.PreviewAsync(
            ProductUrl,
            TestContext.Current.CancellationToken);

        // Assert
        await Assert.ThrowsAsync<WishImportUrlRejectedException>(action);
        VerifyPage();
        _clientMock.VerifyNoOtherCalls();
        _processorMock.VerifyNoOtherCalls();
    }

    private void SetupPage(
        string html,
        string? mediaType = "text/html")
    {
        _clientMock
            .Setup(client => client.DownloadAsync(
                new Uri(ProductUrl),
                2 * 1024 * 1024,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportDocument
            {
                Url = new Uri(ProductUrl),
                Content = Encoding.UTF8.GetBytes(html),
                MediaType = mediaType
            });
    }

    private void VerifyPage(CancellationToken? token = null)
    {
        _clientMock.Verify(
            client => client.DownloadAsync(
                new Uri(ProductUrl),
                2 * 1024 * 1024,
                It.Is<CancellationToken>(actual => token == null || actual == token)),
            Times.Once);
    }

    private static Exception CreateFailure(string failure)
    {

        return failure switch
        {
            "network" => new HttpRequestException("unavailable"),
            "io" => new IOException("unavailable"),
            "timeout" => new OperationCanceledException(),
            "rejected" => new WishImportUrlRejectedException(),
            "invalid" => new GiftImageInvalidException("invalid"),
            _ => new GiftImageUnsupportedFormatException()
        };
    }
}
