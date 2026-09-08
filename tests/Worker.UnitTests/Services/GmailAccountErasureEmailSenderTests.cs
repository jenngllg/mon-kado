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

namespace JennGllg.Fr.MonKado.Back.Worker.UnitTests.Services;

public class GmailAccountErasureEmailSenderTests
{
    private readonly Mock<IGmailApiClient> _gmailClientMock;
    private readonly GmailAccountErasureEmailSender _sender;
    public GmailAccountErasureEmailSenderTests()
    {
        _gmailClientMock = new Mock<IGmailApiClient>(MockBehavior.Strict);
        _sender = new GmailAccountErasureEmailSender(
            _gmailClientMock.Object,
            Microsoft.Extensions.Options.Options.Create(new GmailOptions { SenderAddress = "monkado@example.test" }));
    }

    [Fact]
    public async Task SendAsync_WhenRequested_SendsOneGenericFrenchNotification()
    {
        // Arrange
        var operationId = Guid.CreateVersion7();
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
            operationId,
            "erased@example.test",
            DateTime.UnixEpoch,
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
            "Votre compte a été supprimé – MonKado",
            mime.Subject);
        Assert.Equal(
            "erased@example.test",
            Assert.Single(mime.To.Mailboxes).Address);
        Assert.Equal(
            "monkado@example.test",
            Assert.Single(mime.From.Mailboxes).Address);
        Assert.Equal(
            $"erasure-{operationId:N}@mon-kado.fr",
            mime.MessageId);
        Assert.Equal(
            DateTime.UnixEpoch,
            mime.Date.UtcDateTime);
        Assert.Equal(
            "auto-generated",
            mime.Headers["Auto-Submitted"]);
        Assert.Contains(
            "traitements de nettoyage",
            mime.TextBody);
        Assert.Contains(
            "sauvegardes",
            mime.TextBody);
        Assert.DoesNotContain(
            "erased@example.test",
            mime.TextBody);
        Assert.DoesNotContain(
            "http",
            mime.TextBody);
        Assert.Empty(mime.Attachments);
        _gmailClientMock.Verify(
            client => client.SendAsync(
                encoded,
                cancellationToken),
            Times.Once);
        _gmailClientMock.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, 429, AccountErasureEmailFailure.RateLimited)]
    [InlineData(false, 429, AccountErasureEmailFailure.RateLimited)]
    [InlineData(true, 408, AccountErasureEmailFailure.Transient)]
    [InlineData(false, null, AccountErasureEmailFailure.Transient)]
    [InlineData(true, 500, AccountErasureEmailFailure.Transient)]
    [InlineData(false, 503, AccountErasureEmailFailure.Transient)]
    [InlineData(true, 400, AccountErasureEmailFailure.Rejected)]
    [InlineData(false, 401, AccountErasureEmailFailure.Rejected)]
    public async Task SendAsync_WhenProviderFails_ReturnsOnlyBoundedClassification(
        bool isGmailFailure,
        int? statusCode,
        AccountErasureEmailFailure expectedFailure)
    {
        // Arrange
        var operationId = Guid.CreateVersion7();
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
            operationId,
            "erased@example.test",
            DateTime.UnixEpoch,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<AccountErasureEmailDeliveryException>(action);
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
    [InlineData(true, AccountErasureEmailFailure.Transient)]
    [InlineData(false, AccountErasureEmailFailure.Unexpected)]
    public async Task SendAsync_WhenTransportThrowsWithoutStatus_SanitizesFailure(
        bool isTimeout,
        AccountErasureEmailFailure expectedFailure)
    {
        // Arrange
        var operationId = Guid.CreateVersion7();
        var cancellationToken = TestContext.Current.CancellationToken;
        Exception failure = isTimeout ? new TaskCanceledException("PRIVATE") : new InvalidOperationException("PRIVATE");
        _gmailClientMock
            .Setup(client => client.SendAsync(
                It.IsAny<string>(),
                cancellationToken))
            .ThrowsAsync(failure);

        // Act
        var action = () => _sender.SendAsync(
            operationId,
            "erased@example.test",
            DateTime.UnixEpoch,
            cancellationToken);

        // Assert
        var exception = await Assert.ThrowsAsync<AccountErasureEmailDeliveryException>(action);
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
        var operationId = Guid.CreateVersion7();
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
            operationId,
            "erased@example.test",
            DateTime.UnixEpoch,
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

}
