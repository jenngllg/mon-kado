namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class SafeHttpRequestLoggingExtensionsTests
{
    [Fact]
    public async Task Request_WhenPathAndQueryContainCanaries_LogsOnlyRouteTemplateOrUnmatchedMarker()
    {
        // Arrange
        const string methodCanary = "SENSITIVE-METHOD-CANARY";
        const string pathCanary = "sensitive-path-canary";
        const string queryCanary = "sensitive-query-canary";
        using var factory = new GoogleAuthenticationApiFactory();
        using var client = factory.CreateGoogleClient();
        using var unmatchedRequest = new HttpRequestMessage(
            new HttpMethod(methodCanary),
            $"/{pathCanary}?state={queryCanary}");

        // Act
        using var unmatchedResponse = await client.SendAsync(
            unmatchedRequest,
            TestContext.Current.CancellationToken);
        using var matchedResponse = await client.GetAsync(
            $"/api/v1/auth/google?returnPath=%2Fmy-lists&canary={queryCanary}",
            TestContext.Current.CancellationToken);

        // Assert
        var logs = string.Join(
            Environment.NewLine,
            factory.LogMessages);
        Assert.DoesNotContain(
            methodCanary,
            logs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            pathCanary,
            logs,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            queryCanary,
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "HTTP Other Unmatched completed with status 404.",
            logs,
            StringComparison.Ordinal);
        Assert.Contains(
            "HTTP GET api/v1/auth/google completed with status 302.",
            logs,
            StringComparison.Ordinal);
    }
}
