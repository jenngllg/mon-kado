using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Tests.Common;
using JennGllg.Fr.MonKado.Back.Worker.Exceptions;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Services;

using MimeKit;

using Moq;

using System.Net;
using System.Text.Encodings.Web;

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests.Services;

public class GmailWishlistModerationEmailSenderTests
{
    private readonly Mock<IGmailApiClient> _gmailClientMock;
    private readonly GmailWishlistModerationEmailSender _sender;
    public GmailWishlistModerationEmailSenderTests()
    {
        _gmailClientMock = new Mock<IGmailApiClient>(MockBehavior.Strict);
        _sender = new GmailWishlistModerationEmailSender(
            _gmailClientMock.Object,
            Microsoft.Extensions.Options.Options.Create(new GmailOptions { SenderAddress = "monkado@example.test" }));
    }

    [Theory]
    [InlineData("suspension", "Votre liste a été suspendue")]
    [InlineData("amendment", "Le motif de suspension de votre liste a été modifié")]
    [InlineData("reactivation", "Votre liste a été réactivée")]
    public async Task SendAsync_WhenDecisionIsValid_SendsOneFrenchEscapedNotification(
        string scenario,
        string expectedSubject)
    {
        // Arrange
        var message = scenario switch
        {
            "suspension" => WishlistModerationTestData.CreateSuspensionEmail(),
            "amendment" => WishlistModerationTestData.CreateReasonAmendmentEmail(),
            _ => WishlistModerationTestData.CreateReactivationEmail()
        };
        var cancellationToken = TestContext.Current.CancellationToken;
        string? rawMessage = null;
        _gmailClientMock
            .Setup(client => client.SendAsync(
                It.IsAny<string>(),
                cancellationToken))
            .Callback<string, CancellationToken>((
                raw,
                _) => rawMessage = raw)
            .ReturnsAsync("provider-id");

        // Act
        await _sender.SendAsync(
            message,
            cancellationToken);

        // Assert
        var encoded = Assert.IsType<string>(rawMessage);
        Assert.DoesNotContain(
            '=',
            encoded);
        var base64 = encoded
            .Replace(
            '-',
            '+')
            .Replace(
            '_',
            '/');
        base64 = base64.PadRight(
            (base64.Length + 3) / 4 * 4,
            '=');
        await using var stream = new MemoryStream(Convert.FromBase64String(base64));
        using var mime = await MimeMessage.LoadAsync(
            stream,
            cancellationToken);
        Assert.Equal(
            expectedSubject + " – MonKado",
            mime.Subject);
        Assert.Equal(
            "monkado@example.test",
            Assert
                .Single(mime.From.Mailboxes)
                .Address);
        Assert.Equal(
            message.RecipientAddress,
            Assert
                .Single(mime.To.Mailboxes)
                .Address);
        Assert.Equal(
            $"moderation-{message.EventId:N}@mon-kado.fr",
            mime.MessageId);
        Assert.Equal(
            message.OccurredAt,
            mime.Date.UtcDateTime);
        Assert.Equal(
            "auto-generated",
            mime.Headers["Auto-Submitted"]);
        Assert.Contains(
            message.WishlistName,
            mime.TextBody);
        Assert.Contains(
            HtmlEncoder.Default.Encode(message.WishlistName),
            mime.HtmlBody);
        Assert.DoesNotContain(
            "<script>",
            mime.HtmlBody,
            StringComparison.OrdinalIgnoreCase);

        if (message.Reason is not null)
        {
            Assert.Contains(
                message.Reason,
                mime.TextBody);
            Assert.Contains(
                HtmlEncoder.Default.Encode(message.Reason),
                mime.HtmlBody);
        }
        else
        {
            Assert.DoesNotContain(
                "Motif :",
                mime.TextBody);
            Assert.Contains(
                "Le lien de partage existant est à nouveau utilisable.",
                mime.TextBody);
        }

        _gmailClientMock.Verify(
            client => client.SendAsync(
                encoded,
                cancellationToken),
            Times.Once);
        _gmailClientMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, 429, WishlistModerationEmailFailure.RateLimited)]
    [InlineData(false, 429, WishlistModerationEmailFailure.RateLimited)]
    [InlineData(true, 408, WishlistModerationEmailFailure.Transient)]
    [InlineData(false, null, WishlistModerationEmailFailure.Transient)]
    [InlineData(true, 500, WishlistModerationEmailFailure.Transient)]
    [InlineData(false, 503, WishlistModerationEmailFailure.Transient)]
    [InlineData(true, 400, WishlistModerationEmailFailure.Rejected)]
    [InlineData(false, 401, WishlistModerationEmailFailure.Rejected)]
    public async Task SendAsync_WhenProviderFails_ReturnsOnlyBoundedClassification(
        bool isGmailFailure,
        int? statusCode,
        WishlistModerationEmailFailure expectedFailure)
    {
        // Arrange
        var message = WishlistModerationTestData.CreateSuspensionEmail();
        var cancellationToken = TestContext.Current.CancellationToken;
        var status = (HttpStatusCode?)statusCode;
        var privateFailure = new InvalidOperationException("PRIVATE /storage/secrets owner@example.test");
        Exception failure = isGmailFailure ? new GmailRequestException(
            status,
            TimeSpan.FromMinutes(3),
            privateFailure) : new HttpRequestException(
            "PRIVATE",
            privateFailure,
            status);
        _gmailClientMock
            .Setup(client => client.SendAsync(
                It.IsAny<string>(),
                cancellationToken))
            .ThrowsAsync(failure);

        // Act
        var action = () => _sender.SendAsync(
            message,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<WishlistModerationEmailDeliveryException>(action);
        Assert.Equal(
            expectedFailure,
            exception.Failure);
        Assert.Equal(
            isGmailFailure ? TimeSpan.FromMinutes(3) : null,
            exception.RetryAfter);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(
            "PRIVATE",
            exception.ToString());
        _gmailClientMock.Verify(
            client => client.SendAsync(
                It.IsAny<string>(),
                cancellationToken),
            Times.Once);
        _gmailClientMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, WishlistModerationEmailFailure.Transient)]
    [InlineData(false, WishlistModerationEmailFailure.Unexpected)]
    public async Task SendAsync_WhenTransportThrowsWithoutStatus_SanitizesFailure(
        bool isTimeout,
        WishlistModerationEmailFailure expectedFailure)
    {
        // Arrange
        var message = WishlistModerationTestData.CreateSuspensionEmail();
        var cancellationToken = TestContext.Current.CancellationToken;
        Exception failure = isTimeout ? new TaskCanceledException("PRIVATE") : new InvalidOperationException("PRIVATE");
        _gmailClientMock
            .Setup(client => client.SendAsync(
                It.IsAny<string>(),
                cancellationToken))
            .ThrowsAsync(failure);

        // Act
        var action = () => _sender.SendAsync(
            message,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<WishlistModerationEmailDeliveryException>(action);
        Assert.Equal(
            expectedFailure,
            exception.Failure);
        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(
            "PRIVATE",
            exception.ToString());
        _gmailClientMock.Verify(
            client => client.SendAsync(
                It.IsAny<string>(),
                cancellationToken),
            Times.Once);
        _gmailClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendAsync_WhenCallerCancels_PreservesCancellation()
    {
        // Arrange
        var message = WishlistModerationTestData.CreateSuspensionEmail();
        using var cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _gmailClientMock
            .Setup(client => client.SendAsync(
                It.IsAny<string>(),
                cancellationSource.Token))
            .Returns<string, CancellationToken>((
                _,
                token) =>
            {
                cancellationSource.Cancel();

                return Task.FromCanceled<string>(token);
            });

        // Act
        var action = () => _sender.SendAsync(
            message,
            cancellationSource.Token);

        // Assert
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
        Assert.Equal(
            cancellationSource.Token,
            exception.CancellationToken);
        _gmailClientMock.Verify(
            client => client.SendAsync(
                It.IsAny<string>(),
                cancellationSource.Token),
            Times.Once);
        _gmailClientMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendAsync_WhenDecisionIsUnknown_DoesNotCallProvider()
    {
        // Arrange
        var message = WishlistModerationTestData.CreateUnknownDecisionEmail();

        // Act
        var action = () => _sender.SendAsync(
            message,
            TestContext.Current.CancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<WishlistModerationEmailDeliveryException>(action);
        Assert.Equal(
            WishlistModerationEmailFailure.Unexpected,
            exception.Failure);
        Assert.Null(exception.InnerException);
        _gmailClientMock.VerifyNoOtherCalls();
    }
}
