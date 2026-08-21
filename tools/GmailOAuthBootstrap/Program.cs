using JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

namespace JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var application = new GmailOAuthBootstrapApplication(
            new GmailOAuthAuthorizationBroker(),
            Environment.GetEnvironmentVariable,
            Console.Out,
            Console.Error);

        return await application.RunAsync(CancellationToken.None);
    }
}
