using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Worker.Exceptions;
using JennGllg.Fr.MonKado.Back.Worker.Logging;
using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using MimeKit;

using System.Net;
using System.Text.Encodings.Web;

namespace JennGllg.Fr.MonKado.Back.Worker.Services;

/// <summary>
/// Sends authentication emails through Gmail.
/// </summary>
/// <param name="gmailClient">The Gmail client.</param>
/// <param name="options">The Gmail options.</param>
/// <param name="logger">The logger.</param>
public class GmailAuthenticationEmailSender(
    IGmailApiClient gmailClient,
    IOptions<GmailOptions> options,
    ILogger<GmailAuthenticationEmailSender> logger) : IAuthenticationEmailSender
{
    private readonly GmailOptions _gmail = options.Value;

    /// <inheritdoc />
    public async Task<AuthenticationEmailSendResult> SendEmailConfirmationAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken)
    {

        var result = await SendAsync(
            message.OutboxMessageId,
            token => CreateEmailConfirmationRawMessageAsync(
                message,
                token),
            cancellationToken);
        WorkerLogMessages.AccountConfirmationEmailSent(
            logger,
            message.OutboxMessageId);

        return result;
    }

    /// <inheritdoc />
    public async Task<AuthenticationEmailSendResult> SendEmailChangeConfirmationAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken)
    {

        var result = await SendAsync(
            message.OutboxMessageId,
            token => CreateEmailChangeConfirmationRawMessageAsync(
                message,
                token),
            cancellationToken);
        WorkerLogMessages.MemberEmailChangeConfirmationSent(
            logger,
            message.OutboxMessageId);

        return result;
    }

    /// <inheritdoc />
    public async Task<AuthenticationEmailSendResult> SendEmailChangeSecurityNotificationAsync(
        AuthenticationEmailSecurityNotification message,
        CancellationToken cancellationToken)
    {

        var result = await SendAsync(
            message.OutboxMessageId,
            token => CreateEmailChangeSecurityNotificationRawMessageAsync(
                message,
                token),
            cancellationToken);
        WorkerLogMessages.MemberEmailChangeSecurityNotificationSent(
            logger,
            message.OutboxMessageId);

        return result;
    }

    private async Task<AuthenticationEmailSendResult> SendAsync(
        Guid outboxMessageId,
        Func<CancellationToken, Task<string>> createRawMessageAsync,
        CancellationToken cancellationToken)
    {
        try
        {
            var rawMessage = await createRawMessageAsync(cancellationToken);
            var providerMessageId = await gmailClient.SendAsync(
                rawMessage,
                cancellationToken);

            return new AuthenticationEmailSendResult(providerMessageId);
        }
        catch (GmailRequestException exception)
        {
            var deliveryException = Classify(
                exception.StatusCode,
                exception.RetryAfter,
                exception);
            LogDeliveryFailure(
                outboxMessageId,
                deliveryException);

            throw deliveryException;
        }
        catch (HttpRequestException exception)
        {
            var deliveryException = Classify(
                exception.StatusCode,
                retryAfter: null,
                exception);
            LogDeliveryFailure(
                outboxMessageId,
                deliveryException);

            throw deliveryException;
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            var deliveryException = new AuthenticationEmailDeliveryException(
                AuthenticationEmailFailureCategory.Transient,
                innerException: exception);
            LogDeliveryFailure(
                outboxMessageId,
                deliveryException);

            throw deliveryException;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var deliveryException = new AuthenticationEmailDeliveryException(
                AuthenticationEmailFailureCategory.Unknown,
                innerException: exception);
            LogDeliveryFailure(
                outboxMessageId,
                deliveryException);

            throw deliveryException;
        }
    }

    private void LogDeliveryFailure(
        Guid outboxMessageId,
        AuthenticationEmailDeliveryException exception)
    {
        WorkerLogMessages.AuthenticationEmailProviderRejectedMessage(
            logger,
            outboxMessageId,
            exception.Category);
    }

    private Task<string> CreateEmailConfirmationRawMessageAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken)
    {
        var url = message.ConfirmationUrl.AbsoluteUri;

        return CreateRawMessageAsync(
            message.OutboxMessageId,
            message.RecipientAddress,
            "Confirmez votre adresse e-mail – MonKado",
            CreateAccountConfirmationTextBody(url),
            CreateAccountConfirmationHtmlBody(HtmlEncoder.Default.Encode(url)),
            cancellationToken);
    }

    private Task<string> CreateEmailChangeConfirmationRawMessageAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken)
    {
        var url = message.ConfirmationUrl.AbsoluteUri;

        return CreateRawMessageAsync(
            message.OutboxMessageId,
            message.RecipientAddress,
            "Confirmez votre nouvelle adresse e-mail – MonKado",
            CreateEmailChangeConfirmationTextBody(url),
            CreateEmailChangeConfirmationHtmlBody(HtmlEncoder.Default.Encode(url)),
            cancellationToken);
    }

    private Task<string> CreateEmailChangeSecurityNotificationRawMessageAsync(
        AuthenticationEmailSecurityNotification message,
        CancellationToken cancellationToken)
    {
        var maskedAddress = MaskEmailAddress(message.RequestedAddress);

        return CreateRawMessageAsync(
            message.OutboxMessageId,
            message.RecipientAddress,
            "Demande de changement d’adresse e-mail – MonKado",
            CreateEmailChangeSecurityNotificationTextBody(maskedAddress),
            CreateEmailChangeSecurityNotificationHtmlBody(
                HtmlEncoder.Default.Encode(maskedAddress)),
            cancellationToken);
    }

    private async Task<string> CreateRawMessageAsync(
        Guid outboxMessageId,
        string recipientAddress,
        string subject,
        string textBody,
        string htmlBody,
        CancellationToken cancellationToken)
    {
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(
            "MonKado",
            _gmail.SenderAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(recipientAddress));
        mimeMessage.Subject = subject;
        mimeMessage.MessageId = $"{outboxMessageId:N}@mon-kado.fr";
        mimeMessage.Headers.Add(
            "Auto-Submitted",
            "auto-generated");
        mimeMessage.Body = new BodyBuilder
        {
            TextBody = textBody,
            HtmlBody = htmlBody
        }.ToMessageBody();

        await using var stream = new MemoryStream();
        await mimeMessage.WriteToAsync(
            stream,
            cancellationToken);

        return Convert.ToBase64String(stream.ToArray())
            .TrimEnd('=')
            .Replace(
                '+',
                '-')
            .Replace(
                '/',
                '_');
    }

    private static string CreateAccountConfirmationTextBody(string url)
    {

        return "Bonjour,\n\n" +
            "Confirmez votre adresse e-mail MonKado en ouvrant ce lien :\n" +
            url + "\n\n" +
            "Ce lien est valable pendant 24 heures. " +
            "Si vous n'avez pas créé ce compte, ignorez cet e-mail.";
    }

    private static string CreateAccountConfirmationHtmlBody(string encodedUrl)
    {

        return "<!doctype html><html lang='fr'><body>" +
            "<p>Bonjour,</p><p>Confirmez votre adresse e-mail MonKado :</p>" +
            $"<p><a href='{encodedUrl}'>Confirmer mon adresse e-mail</a></p>" +
            "<p>Ce lien est valable pendant 24 heures.</p>" +
            "<p>Si vous n'avez pas créé ce compte, ignorez cet e-mail.</p>" +
            "</body></html>";
    }

    private static string CreateEmailChangeConfirmationTextBody(string url)
    {

        return "Bonjour,\n\n" +
            "Confirmez cette nouvelle adresse e-mail pour votre compte MonKado :\n" +
            url + "\n\n" +
            "Ce lien est valable pendant 24 heures. " +
            "Si vous n'avez pas demandé ce changement, ignorez cet e-mail.";
    }

    private static string CreateEmailChangeConfirmationHtmlBody(string encodedUrl)
    {

        return "<!doctype html><html lang='fr'><body>" +
            "<p>Bonjour,</p>" +
            "<p>Confirmez cette nouvelle adresse e-mail pour votre compte MonKado :</p>" +
            $"<p><a href='{encodedUrl}'>Confirmer ma nouvelle adresse</a></p>" +
            "<p>Ce lien est valable pendant 24 heures.</p>" +
            "<p>Si vous n'avez pas demandé ce changement, ignorez cet e-mail.</p>" +
            "</body></html>";
    }

    private static string CreateEmailChangeSecurityNotificationTextBody(string maskedAddress)
    {

        return "Bonjour,\n\n" +
            $"Une demande de changement vers l'adresse {maskedAddress} a été créée " +
            "pour votre compte MonKado.\n\n" +
            "Votre adresse actuelle reste active tant que la nouvelle adresse n'est pas confirmée. " +
            "Si vous n'êtes pas à l'origine de cette demande, sécurisez immédiatement votre compte.";
    }

    private static string CreateEmailChangeSecurityNotificationHtmlBody(string maskedAddress)
    {

        return "<!doctype html><html lang='fr'><body>" +
            "<p>Bonjour,</p>" +
            $"<p>Une demande de changement vers l'adresse {maskedAddress} a été créée " +
            "pour votre compte MonKado.</p>" +
            "<p>Votre adresse actuelle reste active tant que la nouvelle adresse n'est pas confirmée.</p>" +
            "<p>Si vous n'êtes pas à l'origine de cette demande, sécurisez immédiatement votre compte.</p>" +
            "</body></html>";
    }

    private static string MaskEmailAddress(string email)
    {
        var separatorIndex = email.IndexOf('@', StringComparison.Ordinal);

        if (separatorIndex <= 0 || separatorIndex == email.Length - 1)
            return "***";

        var localPart = email[..separatorIndex];
        var domain = email[(separatorIndex + 1)..];
        var domainSeparatorIndex = domain.LastIndexOf('.');
        var maskedDomain = domainSeparatorIndex <= 0
            ? $"{domain[0]}***"
            : $"{domain[0]}***{domain[domainSeparatorIndex..]}";

        return $"{localPart[0]}***@{maskedDomain}";
    }

    private static AuthenticationEmailDeliveryException Classify(
        HttpStatusCode? statusCode,
        TimeSpan? retryAfter,
        Exception innerException)
    {
        var category = statusCode switch
        {
            HttpStatusCode.BadRequest => AuthenticationEmailFailureCategory.InvalidRequest,
            HttpStatusCode.Unauthorized => AuthenticationEmailFailureCategory.Authentication,
            HttpStatusCode.Forbidden => AuthenticationEmailFailureCategory.Permission,
            HttpStatusCode.RequestTimeout => AuthenticationEmailFailureCategory.Transient,
            HttpStatusCode.TooManyRequests => AuthenticationEmailFailureCategory.RateLimited,
            >= HttpStatusCode.InternalServerError => AuthenticationEmailFailureCategory.Transient,
            null => AuthenticationEmailFailureCategory.Transient,
            _ => AuthenticationEmailFailureCategory.Unknown
        };

        return new AuthenticationEmailDeliveryException(
            category,
            retryAfter,
            innerException);
    }
}
