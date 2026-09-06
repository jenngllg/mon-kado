namespace JennGllg.Fr.MonKado.Back.Infrastructure.UrlImport.UnitTests;

public class RecordingImportHttpHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<Uri?> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request.RequestUri);

        return Task.FromResult(respond(
                request,
                cancellationToken));
    }
}
