using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.Models;

using System.Text;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RecordingUrlImportClient : IUrlImportClient
{
    public List<Uri> Requests { get; } = [];
    public Exception? Exception
    {
        get; set;
    }
    public string Html { get; set; } = "<title>Imported gift</title>";
    public byte[] ImageContent { get; set; } = [];

    public Task<ImportDocument> DownloadAsync(
        Uri url,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(url);

        if (Exception is not null)
            throw Exception;
        var isImage = url.AbsolutePath.EndsWith(
            ".png",
            StringComparison.Ordinal);

        return Task.FromResult(new ImportDocument
        {
            Url = url,
            Content = isImage ? ImageContent : Encoding.UTF8.GetBytes(Html),
            MediaType = isImage ? "image/png" : "text/html"
        });
    }
}
