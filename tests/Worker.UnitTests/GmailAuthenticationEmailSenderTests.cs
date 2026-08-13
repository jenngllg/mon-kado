using System.Net;
using System.Text;
using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Worker.Email;
using Microsoft.Extensions.Options;
using MimeKit;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public sealed class GmailAuthenticationEmailSenderTests
{
    [Fact]
    public async Task SenderCreatesDeterministicMultipartMessageWithoutTracking()
    {
        CapturingGmailClient client = new();
        GmailAuthenticationEmailSender sender = new(
            client,
            Options.Create(new GmailOptions { SenderAddress = "monkado.app@gmail.com" }));
        Guid outboxId = Guid.Parse("019c52dd-56c1-7cc6-8a95-243f3a032e03");
        Uri confirmationUrl = new(
            "https://mon-kado.fr/confirm-email#userId=019c52dd-56c1-7cc6-8a95-243f3a032e04&token=a-b_c");

        AuthenticationEmailSendResult result = await sender.SendEmailConfirmationAsync(
            new AuthenticationEmailMessage(outboxId, "member@example.fr", confirmationUrl),
            TestContext.Current.CancellationToken);

        Assert.Equal("gmail-message-id", result.ProviderMessageId);
        MimeMessage mime = await Decode(client.RawMessage!);
        Assert.Equal("MonKado", mime.From.Mailboxes.Single().Name);
        Assert.Equal("monkado.app@gmail.com", mime.From.Mailboxes.Single().Address);
        Assert.Equal("member@example.fr", mime.To.Mailboxes.Single().Address);
        Assert.Equal($"{outboxId:N}@mon-kado.fr", mime.MessageId);
        Assert.Equal("auto-generated", mime.Headers["Auto-Submitted"]);
        Assert.Contains(confirmationUrl.AbsoluteUri, mime.TextBody, StringComparison.Ordinal);
        Assert.Contains(
            confirmationUrl.AbsoluteUri.Replace("&", "&amp;", StringComparison.Ordinal),
            mime.HtmlBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain("tracking", mime.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, AuthenticationEmailFailureCategory.InvalidRequest)]
    [InlineData(HttpStatusCode.Unauthorized, AuthenticationEmailFailureCategory.Authentication)]
    [InlineData(HttpStatusCode.Forbidden, AuthenticationEmailFailureCategory.Permission)]
    [InlineData(HttpStatusCode.RequestTimeout, AuthenticationEmailFailureCategory.Transient)]
    [InlineData(HttpStatusCode.TooManyRequests, AuthenticationEmailFailureCategory.RateLimited)]
    [InlineData(HttpStatusCode.InternalServerError, AuthenticationEmailFailureCategory.Transient)]
    [InlineData(HttpStatusCode.NotFound, AuthenticationEmailFailureCategory.Unknown)]
    public async Task SenderClassifiesGmailFailures(
        HttpStatusCode statusCode,
        AuthenticationEmailFailureCategory expectedCategory)
    {
        TimeSpan retryAfter = TimeSpan.FromMinutes(12);
        GmailAuthenticationEmailSender sender = CreateSender(
            new ThrowingGmailClient(new GmailRequestException(statusCode, retryAfter)));

        AuthenticationEmailDeliveryException exception =
            await Assert.ThrowsAsync<AuthenticationEmailDeliveryException>(() =>
                sender.SendEmailConfirmationAsync(CreateMessage(), TestContext.Current.CancellationToken));

        Assert.Equal(expectedCategory, exception.Category);
        Assert.Equal(retryAfter, exception.RetryAfter);
    }

    [Fact]
    public async Task SenderClassifiesNetworkFailureFromHttpClient()
    {
        GmailAuthenticationEmailSender sender = CreateSender(
            new ThrowingGmailClient(new HttpRequestException(
                "Network failure.",
                inner: null,
                HttpStatusCode.ServiceUnavailable)));

        AuthenticationEmailDeliveryException exception =
            await Assert.ThrowsAsync<AuthenticationEmailDeliveryException>(() =>
                sender.SendEmailConfirmationAsync(CreateMessage(), TestContext.Current.CancellationToken));

        Assert.Equal(AuthenticationEmailFailureCategory.Transient, exception.Category);
    }

    [Fact]
    public async Task SenderClassifiesProviderTimeoutAsTransient()
    {
        GmailAuthenticationEmailSender sender = CreateSender(
            new ThrowingGmailClient(new TaskCanceledException("Provider timeout.")));

        AuthenticationEmailDeliveryException exception =
            await Assert.ThrowsAsync<AuthenticationEmailDeliveryException>(() =>
                sender.SendEmailConfirmationAsync(CreateMessage(), TestContext.Current.CancellationToken));

        Assert.Equal(AuthenticationEmailFailureCategory.Transient, exception.Category);
    }

    [Fact]
    public async Task SenderClassifiesUnexpectedFailureWithoutLeakingDetails()
    {
        GmailAuthenticationEmailSender sender = CreateSender(
            new ThrowingGmailClient(new InvalidOperationException("Sensitive provider detail.")));

        AuthenticationEmailDeliveryException exception =
            await Assert.ThrowsAsync<AuthenticationEmailDeliveryException>(() =>
                sender.SendEmailConfirmationAsync(CreateMessage(), TestContext.Current.CancellationToken));

        Assert.Equal(AuthenticationEmailFailureCategory.Unknown, exception.Category);
        Assert.DoesNotContain("Sensitive", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SenderPreservesCallerCancellation()
    {
        using CancellationTokenSource source = new();
        source.Cancel();
        GmailAuthenticationEmailSender sender = CreateSender(
            new ThrowingGmailClient(new OperationCanceledException(source.Token)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sender.SendEmailConfirmationAsync(CreateMessage(), source.Token));
    }

    private static GmailAuthenticationEmailSender CreateSender(IGmailApiClient client) =>
        new(client, Options.Create(new GmailOptions { SenderAddress = "monkado.app@gmail.com" }));

    private static AuthenticationEmailMessage CreateMessage() =>
        new(
            Guid.Parse("019c52dd-56c1-7cc6-8a95-243f3a032e03"),
            "member@example.fr",
            new Uri("https://mon-kado.fr/confirm-email#token=value"));

    private static async Task<MimeMessage> Decode(string raw)
    {
        string base64 = raw.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
        await using MemoryStream stream = new(Convert.FromBase64String(base64));
        return await MimeMessage.LoadAsync(stream, TestContext.Current.CancellationToken);
    }

    private sealed class CapturingGmailClient : IGmailApiClient
    {
        public string? RawMessage { get; private set; }

        public Task<string> SendAsync(string rawMessage, CancellationToken cancellationToken)
        {
            RawMessage = rawMessage;
            return Task.FromResult("gmail-message-id");
        }
    }

    private sealed class ThrowingGmailClient(Exception exception) : IGmailApiClient
    {
        public Task<string> SendAsync(string rawMessage, CancellationToken cancellationToken) =>
            Task.FromException<string>(exception);
    }
}
