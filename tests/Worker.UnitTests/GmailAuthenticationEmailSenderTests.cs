using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Constants;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Worker.Exceptions;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Services;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using MimeKit;

using System.Net;
using System.Text;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests;

public class GmailAuthenticationEmailSenderTests
{
    [Fact]
    public async Task SendEmailConfirmationAsync_WhenSender_CreatesDeterministicMultipartMessageWithoutTracking()
    {
        // Arrange
        var client = new CapturingGmailClient();
        var logger = new RecordingLogger<GmailAuthenticationEmailSender>();
        var sender = new GmailAuthenticationEmailSender(
            client,
            Microsoft.Extensions.Options.Options.Create(
                new GmailOptions { SenderAddress = "monkado.app@gmail.com" }),
            logger);
        var outboxId = Guid.Parse("019c52dd-56c1-7cc6-8a95-243f3a032e03");
        var confirmationUrl = new Uri(
            "https://mon-kado.fr/confirm-email#userId=019c52dd-56c1-7cc6-8a95-243f3a032e04&token=a-b_c");

        // Act
        var result = await sender.SendEmailConfirmationAsync(
            new AuthenticationEmailMessage(
                outboxId,
                "member@example.fr",
                confirmationUrl),
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "gmail-message-id",
            result.ProviderMessageId);
        var mime = await DecodeAsync(client.RawMessage!);
        Assert.Equal(
            "MonKado",
            mime.From.Mailboxes.Single().Name);
        Assert.Equal(
            "monkado.app@gmail.com",
            mime.From.Mailboxes.Single().Address);
        Assert.Equal(
            "member@example.fr",
            mime.To.Mailboxes.Single().Address);
        Assert.Equal(
            $"{outboxId:N}@mon-kado.fr",
            mime.MessageId);
        Assert.Equal(
            "auto-generated",
            mime.Headers["Auto-Submitted"]);
        Assert.Contains(
            confirmationUrl.AbsoluteUri,
            mime.TextBody,
            StringComparison.Ordinal);
        Assert.Contains(
            confirmationUrl.AbsoluteUri.Replace(
                "&",
                "&amp;",
                StringComparison.Ordinal),
            mime.HtmlBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "tracking",
            mime.HtmlBody,
            StringComparison.OrdinalIgnoreCase);
        var log = Assert.Single(logger.Entries);
        Assert.Equal(
            LogLevel.Information,
            log.LogLevel);
        Assert.Equal(
            LogEventIds.AccountConfirmationEmailSent,
            log.EventId.Id);
        Assert.Contains(
            outboxId.ToString(),
            log.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        HttpStatusCode.BadRequest,
        AuthenticationEmailFailureCategory.InvalidRequest)]
    [InlineData(
        HttpStatusCode.Unauthorized,
        AuthenticationEmailFailureCategory.Authentication)]
    [InlineData(
        HttpStatusCode.Forbidden,
        AuthenticationEmailFailureCategory.Permission)]
    [InlineData(
        HttpStatusCode.RequestTimeout,
        AuthenticationEmailFailureCategory.Transient)]
    [InlineData(
        HttpStatusCode.TooManyRequests,
        AuthenticationEmailFailureCategory.RateLimited)]
    [InlineData(
        HttpStatusCode.InternalServerError,
        AuthenticationEmailFailureCategory.Transient)]
    [InlineData(
        HttpStatusCode.NotFound,
        AuthenticationEmailFailureCategory.Unknown)]
    public async Task SendEmailConfirmationAsync_WhenSenderClassifiesGmailFailures_Completes(
        HttpStatusCode statusCode,
        AuthenticationEmailFailureCategory expectedCategory)
    {
        // Arrange
        // Act
        var retryAfter = TimeSpan.FromMinutes(12);
        var logger = new RecordingLogger<GmailAuthenticationEmailSender>();
        var sender = CreateSender(
            new ThrowingGmailClient(new GmailRequestException(
                statusCode,
                retryAfter)),
            logger);

        var exception =
            await Assert.ThrowsAsync<AuthenticationEmailDeliveryException>(() =>
                sender.SendEmailConfirmationAsync(
                    CreateMessage(),
                    TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            expectedCategory,
            exception.Category);
        Assert.Equal(
            retryAfter,
            exception.RetryAfter);
        var log = Assert.Single(logger.Entries);
        Assert.Equal(
            LogLevel.Error,
            log.LogLevel);
        Assert.Equal(
            LogEventIds.AuthenticationEmailProviderRejectedMessage,
            log.EventId.Id);
        Assert.DoesNotContain(
            "member@example.fr",
            log.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "token=value",
            log.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendEmailConfirmationAsync_WhenSenderClassifiesNetworkFailureFromHttpClient_Completes()
    {
        // Arrange
        // Act
        var sender = CreateSender(
            new ThrowingGmailClient(new HttpRequestException(
                "Network failure.",
                inner: null,
                HttpStatusCode.ServiceUnavailable)));

        var exception =
            await Assert.ThrowsAsync<AuthenticationEmailDeliveryException>(() =>
                sender.SendEmailConfirmationAsync(
                    CreateMessage(),
                    TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            AuthenticationEmailFailureCategory.Transient,
            exception.Category);
    }

    [Fact]
    public async Task SendEmailConfirmationAsync_WhenGmailFailureHasNoStatus_ClassifiesAsTransient()
    {
        // Arrange
        var sender = CreateSender(
            new ThrowingGmailClient(new GmailRequestException(
                statusCode: null,
                retryAfter: null)));

        // Act
        var exception = await Assert.ThrowsAsync<AuthenticationEmailDeliveryException>(() =>
            sender.SendEmailConfirmationAsync(
                CreateMessage(),
                TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            AuthenticationEmailFailureCategory.Transient,
            exception.Category);
    }

    [Fact]
    public async Task SendEmailConfirmationAsync_WhenSenderClassifiesProviderTimeoutAsTransient_Completes()
    {
        // Arrange
        // Act
        var sender = CreateSender(
            new ThrowingGmailClient(new TaskCanceledException("Provider timeout.")));

        var exception =
            await Assert.ThrowsAsync<AuthenticationEmailDeliveryException>(() =>
                sender.SendEmailConfirmationAsync(
                    CreateMessage(),
                    TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            AuthenticationEmailFailureCategory.Transient,
            exception.Category);
    }

    [Fact]
    public async Task SendEmailConfirmationAsync_WhenSenderClassifiesUnexpectedFailureWithoutLeakingDetails_Completes()
    {
        // Arrange
        // Act
        var sender = CreateSender(
            new ThrowingGmailClient(new InvalidOperationException("Sensitive provider detail.")));

        var exception =
            await Assert.ThrowsAsync<AuthenticationEmailDeliveryException>(() =>
                sender.SendEmailConfirmationAsync(
                    CreateMessage(),
                    TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(
            AuthenticationEmailFailureCategory.Unknown,
            exception.Category);
        Assert.DoesNotContain(
            "Sensitive",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendEmailConfirmationAsync_WhenSender_PreservesCallerCancellation()
    {
        // Arrange
        // Act
        using var source = new CancellationTokenSource();
        source.Cancel();
        var sender = CreateSender(
            new ThrowingGmailClient(new OperationCanceledException(source.Token)));

        // Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sender.SendEmailConfirmationAsync(
                CreateMessage(),
                source.Token));
    }

    [Fact]
    public async Task SendEmailChangeConfirmationAsync_WhenCalled_CreatesDedicatedConfirmationMessage()
    {
        // Arrange
        var client = new CapturingGmailClient();
        var logger = new RecordingLogger<GmailAuthenticationEmailSender>();
        var sender = CreateSender(
            client,
            logger);
        var confirmationUrl = new Uri(
            "https://mon-kado.fr/confirm-email-change#requestId=request&token=a-b_c");
        var message = new AuthenticationEmailMessage(
            Guid.CreateVersion7(),
            "new@example.fr",
            confirmationUrl);

        // Act
        var result = await sender.SendEmailChangeConfirmationAsync(
            message,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "gmail-message-id",
            result.ProviderMessageId);
        var mime = await DecodeAsync(client.RawMessage ?? string.Empty);
        Assert.Equal(
            "new@example.fr",
            mime.To.Mailboxes.Single().Address);
        Assert.Contains(
            "nouvelle adresse",
            mime.Subject,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            confirmationUrl.AbsoluteUri,
            mime.TextBody,
            StringComparison.Ordinal);
        Assert.Contains(
            confirmationUrl.AbsoluteUri.Replace(
                "&",
                "&amp;",
                StringComparison.Ordinal),
            mime.HtmlBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "tracking",
            mime.HtmlBody,
            StringComparison.OrdinalIgnoreCase);
        var log = Assert.Single(logger.Entries);
        Assert.Equal(
            LogEventIds.MemberEmailChangeConfirmationSent,
            log.EventId.Id);
    }

    [Theory]
    [InlineData("new@example.fr", "n***@e***.fr")]
    [InlineData("new@example", "n***@e***")]
    [InlineData("invalid", "***")]
    public async Task SendEmailChangeSecurityNotificationAsync_WhenCalled_MasksRequestedAddress(
        string requestedAddress,
        string expectedMaskedAddress)
    {
        // Arrange
        var client = new CapturingGmailClient();
        var logger = new RecordingLogger<GmailAuthenticationEmailSender>();
        var sender = CreateSender(
            client,
            logger);
        var message = new AuthenticationEmailSecurityNotification(
            Guid.CreateVersion7(),
            "old@example.fr",
            requestedAddress);

        // Act
        var result = await sender.SendEmailChangeSecurityNotificationAsync(
            message,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "gmail-message-id",
            result.ProviderMessageId);
        var mime = await DecodeAsync(client.RawMessage ?? string.Empty);
        Assert.Equal(
            "old@example.fr",
            mime.To.Mailboxes.Single().Address);
        Assert.Contains(
            expectedMaskedAddress,
            mime.TextBody,
            StringComparison.Ordinal);
        Assert.Contains(
            expectedMaskedAddress,
            mime.HtmlBody,
            StringComparison.Ordinal);

        if (requestedAddress != "invalid")
        {
            Assert.DoesNotContain(
                requestedAddress,
                mime.TextBody,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                requestedAddress,
                mime.HtmlBody,
                StringComparison.Ordinal);
        }

        var log = Assert.Single(logger.Entries);
        Assert.Equal(
            LogEventIds.MemberEmailChangeSecurityNotificationSent,
            log.EventId.Id);
    }

    [Fact]
    public async Task SendPasswordChangedSecurityNotificationAsync_WhenCalled_CreatesDedicatedNotification()
    {
        // Arrange
        var client = new CapturingGmailClient();
        var logger = new RecordingLogger<GmailAuthenticationEmailSender>();
        var sender = CreateSender(
            client,
            logger);
        var outboxMessageId = Guid.CreateVersion7();
        var changedAt = new DateTime(
            2026,
            8,
            23,
            10,
            15,
            0,
            DateTimeKind.Utc);
        var message = new AuthenticationPasswordChangedNotification(
            outboxMessageId,
            "member@example.fr",
            changedAt);

        // Act
        var result = await sender.SendPasswordChangedSecurityNotificationAsync(
            message,
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(
            "gmail-message-id",
            result.ProviderMessageId);
        var mime = await DecodeAsync(client.RawMessage ?? string.Empty);
        Assert.Equal(
            "member@example.fr",
            mime.To.Mailboxes.Single().Address);
        Assert.Contains(
            "mot de passe",
            mime.Subject,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "2026-08-23T10:15:00.0000000Z",
            mime.TextBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "Toutes vos sessions ont été déconnectées",
            mime.HtmlBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "password",
            mime.TextBody,
            StringComparison.OrdinalIgnoreCase);
        var log = Assert.Single(logger.Entries);
        Assert.Equal(
            LogEventIds.MemberPasswordChangedSecurityNotificationSent,
            log.EventId.Id);
        Assert.Contains(
            outboxMessageId.ToString(),
            log.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "member@example.fr",
            log.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    private static GmailAuthenticationEmailSender CreateSender(
        IGmailApiClient client,
        ILogger<GmailAuthenticationEmailSender>? logger = null)
    {

        return new(
            client,
            Microsoft.Extensions.Options.Options.Create(
                new GmailOptions { SenderAddress = "monkado.app@gmail.com" }),
            logger ?? NullLogger<GmailAuthenticationEmailSender>.Instance);
    }

    private static AuthenticationEmailMessage CreateMessage()
    {

        return new(
            Guid.Parse("019c52dd-56c1-7cc6-8a95-243f3a032e03"),
            "member@example.fr",
            new Uri("https://mon-kado.fr/confirm-email#token=value"));
    }

    private static async Task<MimeMessage> DecodeAsync(string raw)
    {
        var base64 = raw.Replace(
            '-',
            '+').Replace(
                '_',
                '/');
        base64 = base64.PadRight(
            base64.Length + ((4 - (base64.Length % 4)) % 4),
            '=');
        await using var stream = new MemoryStream(Convert.FromBase64String(base64));

        return await MimeMessage.LoadAsync(
            stream,
            TestContext.Current.CancellationToken);
    }

}
