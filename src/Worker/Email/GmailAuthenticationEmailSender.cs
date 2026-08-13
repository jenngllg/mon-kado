using System.Net;
using System.Text.Encodings.Web;
using JennGllg.Fr.MonKado.Back.Application.Accounts;
using Microsoft.Extensions.Options;
using MimeKit;

namespace JennGllg.Fr.MonKado.Back.Worker.Email;

internal sealed class GmailAuthenticationEmailSender(
    IGmailApiClient gmailClient,
    IOptions<GmailOptions> options) : IAuthenticationEmailSender
{
    private readonly GmailOptions gmail = options.Value;

    public async Task<AuthenticationEmailSendResult> SendEmailConfirmationAsync(
        AuthenticationEmailMessage delivery,
        CancellationToken cancellationToken)
    {
        try
        {
            string rawMessage = await CreateRawMessage(delivery, cancellationToken);
            string providerMessageId = await gmailClient.SendAsync(rawMessage, cancellationToken);
            return new AuthenticationEmailSendResult(providerMessageId);
        }
        catch (GmailRequestException exception)
        {
            throw Classify(exception.StatusCode, exception.RetryAfter, exception);
        }
        catch (HttpRequestException exception)
        {
            throw Classify(exception.StatusCode, retryAfter: null, exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AuthenticationEmailDeliveryException(
                AuthenticationEmailFailureCategory.Transient,
                innerException: exception);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new AuthenticationEmailDeliveryException(
                AuthenticationEmailFailureCategory.Unknown,
                innerException: exception);
        }
    }

    private async Task<string> CreateRawMessage(
        AuthenticationEmailMessage delivery,
        CancellationToken cancellationToken)
    {
        string url = delivery.ConfirmationUrl.AbsoluteUri;
        MimeMessage message = new();
        message.From.Add(new MailboxAddress("MonKado", gmail.SenderAddress!));
        message.To.Add(MailboxAddress.Parse(delivery.RecipientAddress));
        message.Subject = "Confirmez votre adresse e-mail \u2013 MonKado";
        message.MessageId = $"{delivery.OutboxMessageId:N}@mon-kado.fr";
        message.Headers.Add("Auto-Submitted", "auto-generated");
        message.Body = new BodyBuilder
        {
            TextBody = CreateTextBody(url),
            HtmlBody = CreateHtmlBody(HtmlEncoder.Default.Encode(url))
        }.ToMessageBody();

        await using MemoryStream stream = new();
        await message.WriteToAsync(stream, cancellationToken);
        return Convert.ToBase64String(stream.ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string CreateTextBody(string url) =>
        "Bonjour,\n\n" +
        "Confirmez votre adresse e-mail MonKado en ouvrant ce lien :\n" +
        url + "\n\n" +
        "Ce lien est valable pendant 24 heures. " +
        "Si vous n'avez pas cr\u00e9\u00e9 ce compte, ignorez cet e-mail.";

    private static string CreateHtmlBody(string encodedUrl) =>
        "<!doctype html><html lang='fr'><body>" +
        "<p>Bonjour,</p><p>Confirmez votre adresse e-mail MonKado :</p>" +
        $"<p><a href='{encodedUrl}'>Confirmer mon adresse e-mail</a></p>" +
        "<p>Ce lien est valable pendant 24 heures.</p>" +
        "<p>Si vous n'avez pas cr\u00e9\u00e9 ce compte, ignorez cet e-mail.</p>" +
        "</body></html>";

    private static AuthenticationEmailDeliveryException Classify(
        HttpStatusCode? statusCode,
        TimeSpan? retryAfter,
        Exception innerException)
    {
        AuthenticationEmailFailureCategory category = statusCode switch
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
        return new AuthenticationEmailDeliveryException(category, retryAfter, innerException);
    }
}
