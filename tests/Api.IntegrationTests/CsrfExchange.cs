namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class CsrfExchange(
    HttpClient client,
    string token,
    string cookie)
{
    public HttpClient Client { get; } = client;

    public string Token { get; } = token;

    public string Cookie { get; } = cookie;
}
