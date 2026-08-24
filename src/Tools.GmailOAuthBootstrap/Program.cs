using JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

namespace JennGllg.Fr.MonKado.Back.Tools.GmailOAuthBootstrap;

/// <summary>
/// Provides the Gmail OAuth bootstrap entry point.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the Gmail OAuth bootstrap process.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The process exit code.</returns>
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
