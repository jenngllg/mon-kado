using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Worker.Exceptions;
using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.Options;

using MimeKit;

using System.Net;
using System.Text.Encodings.Web;

namespace JennGllg.Fr.MonKado.Back.Worker.Services;
/// <summary>
/// Represents gmail authentication email sender.
/// </summary>
/// <param name="gmailClient">The gmail client.</param>
/// <param name="options">The options.</param>

public class GmailAuthenticationEmailSender(
    IGmailApiClient gmailClient,
    IOptions<GmailOptions> options) : IAuthenticationEmailSender
{
    private readonly GmailOptions _gmail = options.Value;
    /// <summary>
    /// Executes the send email confirmation async operation.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public async Task<AuthenticationEmailSendResult> SendEmailConfirmationAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            var rawMessage = await CreateRawMessageAsync(
                message,
                cancellationToken);
            var providerMessageId = await gmailClient.SendAsync(
                rawMessage,
                cancellationToken);

            return new AuthenticationEmailSendResult(providerMessageId);
        }
        catch (GmailRequestException exception)
        {

            throw Classify(
                exception.StatusCode,
                exception.RetryAfter,
                exception);
        }
        catch (HttpRequestException exception)
        {

            throw Classify(
                exception.StatusCode,
                retryAfter: null,
                exception);
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

    private async Task<string> CreateRawMessageAsync(
        AuthenticationEmailMessage delivery,
        CancellationToken cancellationToken)
    {
        var url = delivery.ConfirmationUrl.AbsoluteUri;
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(
            "MonKado",
            _gmail.SenderAddress!));
        mimeMessage.To.Add(MailboxAddress.Parse(delivery.RecipientAddress));
        mimeMessage.Subject = "Confirmez votre adresse e-mail \u2013 MonKado";
        mimeMessage.MessageId = $"{delivery.OutboxMessageId:N}@mon-kado.fr";
        mimeMessage.Headers.Add(
            "Auto-Submitted",
            "auto-generated");
        mimeMessage.Body = new BodyBuilder
        {
            TextBody = CreateTextBody(url),
            HtmlBody = CreateHtmlBody(HtmlEncoder.Default.Encode(url))
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

    private static string CreateTextBody(string url)
    {

        return "Bonjour,\n\n" +
        "Confirmez votre adresse e-mail MonKado en ouvrant ce lien :\n" +
        url + "\n\n" +
        "Ce lien est valable pendant 24 heures. " +
        "Si vous n'avez pas cr\u00e9\u00e9 ce compte, ignorez cet e-mail.";
    }

    private static string CreateHtmlBody(string encodedUrl)
    {

        return "<!doctype html><html lang='fr'><body>" +
        "<p>Bonjour,</p><p>Confirmez votre adresse e-mail MonKado :</p>" +
        $"<p><a href='{encodedUrl}'>Confirmer mon adresse e-mail</a></p>" +
        "<p>Ce lien est valable pendant 24 heures.</p>" +
        "<p>Si vous n'avez pas cr\u00e9\u00e9 ce compte, ignorez cet e-mail.</p>" +
        "</body></html>";
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
