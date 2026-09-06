using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Options;

using Microsoft.Extensions.Options;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Services;

/// <summary>Downloads bounded documents while validating every redirect destination.</summary>
/// <param name="httpClient">The public-only typed HTTP client.</param>
/// <param name="options">The validated import limits.</param>
public class UrlImportClient(
    HttpClient httpClient,
    IOptions<UrlImportOptions> options) : IUrlImportClient
{
    /// <inheritdoc/>
    public async Task<ImportDocument> DownloadAsync(
        Uri url,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ImportDocument document;
        for (var redirects = 0; ; redirects++)
        {

            if (!WishImportUrlValidation.IsValid(url.AbsoluteUri))
                throw new WishImportUrlRejectedException();
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                url);
            using var response = await SendAsync(
                request,
                cancellationToken);

            if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
            {

                if (redirects >= options.Value.MaximumRedirects || response.Headers.Location is null)
                    throw new HttpRequestException("The merchant redirect limit was reached.");

                if (!Uri.TryCreate(
                    url,
                    response.Headers.Location,
                    out var destination))
                    throw new HttpRequestException("The merchant redirect destination is invalid.");
                url = destination;
                continue;
            }

            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > maximumBytes)
                throw new HttpRequestException("The merchant response exceeds the size limit.");
            using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var content = new MemoryStream();
            var buffer = new byte[8192];
            while (true)
            {
                var read = await source.ReadAsync(
                    buffer.AsMemory(
                        0,
                        Math.Min(
                            buffer.Length,
                            maximumBytes - (int)content.Length + 1)),
                    cancellationToken);

                if (read == 0)
                    break;

                if (content.Length + read > maximumBytes)
                    throw new HttpRequestException("The merchant response exceeds the size limit.");
                content.Write(
                    buffer,
                    0,
                    read);
            }

            document = new ImportDocument
            {
                Url = url,
                Content = content.ToArray(),
                MediaType = response.Content.Headers.ContentType?.MediaType,
                Charset = response.Content.Headers.ContentType?.CharSet
            };
            break;
        }

        return document;
    }

    /// <summary>Preserves safe destination rejection through the HTTP transport wrapper.</summary>
    /// <param name="request">The bounded request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The response with headers available before buffering.</returns>
    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {

            return await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.InnerException is WishImportUrlRejectedException)
        {

            throw new WishImportUrlRejectedException();
        }
    }
}
