using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;

using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

/// <summary>Produces bounded in-memory suggestions without changing persistence or image storage.</summary>
/// <param name="client">The safe merchant client.</param>
/// <param name="extractor">The passive metadata extractor.</param>
/// <param name="imageProcessor">The existing normalized image processor.</param>
/// <param name="options">The validated work limits.</param>
/// <param name="timeProvider">The shared controllable clock.</param>
public class WishImportService(
    IUrlImportClient client,
    IMerchantMetadataExtractor extractor,
    IGiftImageProcessor imageProcessor,
    IOptions<UrlImportOptions> options,
    TimeProvider timeProvider) : IWishImportService
{
    /// <inheritdoc/>
    public async Task<WishImportPreview> PreviewAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(
            TimeSpan.FromSeconds(options.Value.TimeoutSeconds),
            timeProvider);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeout.Token);
        var warnings = new List<string>();
        var metadata = new MerchantMetadata();
        try
        {
            var document = await client.DownloadAsync(
                new Uri(url),
                options.Value.MaximumHtmlBytes,
                budget.Token);

            if (!string.Equals(
                document.MediaType,
                "text/html",
                StringComparison.OrdinalIgnoreCase) && !string.Equals(
                document.MediaType,
                "application/xhtml+xml",
                StringComparison.OrdinalIgnoreCase))
                throw new HttpRequestException("The merchant response is not HTML.");
            metadata = extractor.Extract(document);
            budget.Token.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (IsRemoteFailure(
            exception,
            cancellationToken))
        {
            warnings.Add(WishImportWarnings.PageUnavailable);
        }

        var image = await ReadImageAsync(
            metadata.ImageUrl,
            warnings,
            budget.Token,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (metadata.Name is null)
            warnings.Add(WishImportWarnings.NameUnavailable);

        if (metadata.Price is null)
            warnings.Add(metadata.CurrencyUnsupported ? WishImportWarnings.CurrencyUnsupported : WishImportWarnings.PriceUnavailable);

        return new WishImportPreview
        {
            Name = metadata.Name,
            Url = url,
            Price = metadata.Price,
            Quantity = 1,
            Image = image,
            Warnings = warnings.ToArray()
        };
    }

    /// <summary>Normalizes at most one image; failure never discards valid text suggestions.</summary>
    /// <param name="url">The optional merchant image URL.</param>
    /// <param name="warnings">The accumulated safe warnings.</param>
    /// <param name="budgetToken">The remaining total work budget.</param>
    /// <param name="cancellationToken">The caller cancellation token.</param>
    /// <returns>The normalized image, or no image.</returns>
    private async Task<WishImportImage?> ReadImageAsync(
        Uri? url,
        List<string> warnings,
        CancellationToken budgetToken,
        CancellationToken cancellationToken)
    {

        if (url is null)
        {
            warnings.Add(WishImportWarnings.ImageUnavailable);

            return null;
        }

        try
        {
            var document = await client.DownloadAsync(
                url,
                GiftImageConstraints.MaximumInputLength,
                budgetToken);
            var image = await imageProcessor.ProcessAsync(
                document.Content,
                budgetToken);
            budgetToken.ThrowIfCancellationRequested();

            if (image.Content.Length > GiftImageConstraints.MaximumInputLength)
                throw new GiftImageInvalidException("The normalized image exceeds the upload limit.");

            return new WishImportImage
            {
                ContentBase64 = Convert.ToBase64String(image.Content.Span),
                ContentType = "image/webp"
            };
        }
        catch (Exception exception) when (exception is WishImportUrlRejectedException or GiftImageInvalidException or GiftImageUnsupportedFormatException || IsRemoteFailure(
            exception,
            cancellationToken))
        {
            warnings.Add(WishImportWarnings.ImageUnavailable);

            return null;
        }
    }

    /// <summary>Recognizes expected remote failures while preserving caller cancellation.</summary>
    /// <param name="exception">The remote failure.</param>
    /// <param name="cancellationToken">The original caller token.</param>
    /// <returns>Whether manual completion should be offered.</returns>
    private static bool IsRemoteFailure(
        Exception exception,
        CancellationToken cancellationToken)
    {

        return exception is HttpRequestException or IOException || exception is OperationCanceledException && !cancellationToken.IsCancellationRequested;
    }
}
