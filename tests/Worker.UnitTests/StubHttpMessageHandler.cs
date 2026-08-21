namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

internal class StubHttpMessageHandler(
    Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {

        return handler(
            request,
            cancellationToken);
    }
}
