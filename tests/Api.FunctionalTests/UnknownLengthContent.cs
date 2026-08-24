using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class UnknownLengthContent : HttpContent
{
    private readonly byte[] _content;

    public UnknownLengthContent(string value)
    {
        _content = Encoding.UTF8.GetBytes(value);
        Headers.ContentType = new MediaTypeHeaderValue(
            "application/x-www-form-urlencoded");
    }

    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context)
    {
        _ = context;

        return stream.WriteAsync(_content).AsTask();
    }

    protected override Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context,
        CancellationToken cancellationToken)
    {
        _ = context;

        return stream.WriteAsync(
            _content,
            cancellationToken)
            .AsTask();
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;

        return false;
    }
}
