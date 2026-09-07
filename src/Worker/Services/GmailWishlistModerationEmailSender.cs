using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Domain.Enums;
using JennGllg.Fr.MonKado.Back.Worker.Exceptions;
using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.Options;

using MimeKit;

using System.Globalization;
using System.Net;
using System.Text.Encodings.Web;

namespace JennGllg.Fr.MonKado.Back.Worker.Services;

/// <summary>Sends French moderation notifications through the existing Gmail transport.</summary>
/// <param name="gmailClient">The Gmail client without transparent POST retries.</param>
/// <param name="options">The validated sender configuration.</param>
public class GmailWishlistModerationEmailSender(
    IGmailApiClient gmailClient,
    IOptions<GmailOptions> options) : IWishlistModerationEmailSender
{
    /// <summary>Encodes a private notification and sends it once with a stable event-based Message-ID.</summary>
    /// <param name="message">The current-recipient historical decision.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task completed after the provider acknowledgement.</returns>
    /// <exception cref="WishlistModerationEmailDeliveryException">The provider rejected or failed the send.</exception>
    public async Task SendAsync(
        WishlistModerationEmailMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            var raw = await CreateRawMessageAsync(
                message,
                cancellationToken);
            await gmailClient.SendAsync(
                raw,
                cancellationToken);
        }
        catch (GmailRequestException exception)
        {

            throw Classify(
                exception.StatusCode,
                exception.RetryAfter);
        }
        catch (HttpRequestException exception)
        {

            throw Classify(
                exception.StatusCode,
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {

            throw;
        }
        catch (OperationCanceledException)
        {

            throw new WishlistModerationEmailDeliveryException(
                WishlistModerationEmailFailure.Transient,
                null);
        }
        catch (Exception)
        {

            throw new WishlistModerationEmailDeliveryException(
                WishlistModerationEmailFailure.Unexpected,
                null);
        }
    }

    /// <summary>Builds an encoded text and HTML MIME message without retaining provider data.</summary>
    /// <param name="message">The private notification.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The URL-safe Base64 MIME payload.</returns>
    private async Task<string> CreateRawMessageAsync(
        WishlistModerationEmailMessage message,
        CancellationToken cancellationToken)
    {
        var decision = message.Action switch
        {
            WishlistModerationAction.Suspended => "Votre liste a été suspendue",
            WishlistModerationAction.ReasonUpdated => "Le motif de suspension de votre liste a été modifié",
            WishlistModerationAction.Reactivated => "Votre liste a été réactivée",
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        var date = message.OccurredAt.ToString(
            "dd/MM/yyyy HH:mm:ss 'UTC'",
            CultureInfo.InvariantCulture);
        var details = message.Action is WishlistModerationAction.Reactivated ? "Le lien de partage existant est à nouveau utilisable." : $"Motif : {message.Reason}\nLa liste reste consultable en lecture seule dans votre compte.";
        var textBody = $"Bonjour,\n\n{decision}.\nListe : {message.WishlistName} ({message.WishlistId})\n" + $"Date de décision : {date}\n\n{details}\n\nL'équipe MonKado";
        var htmlBody = "<!doctype html><html lang=\"fr\"><body><p>" + HtmlEncoder.Default
            .Encode(textBody)
            .Replace(
            "\n",
            "<br>",
            StringComparison.Ordinal) + "</p></body></html>";
        using var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(
                "MonKado",
                options.Value.SenderAddress));
        mimeMessage.To.Add(MailboxAddress.Parse(message.RecipientAddress));
        mimeMessage.Subject = $"{decision} – MonKado";
        mimeMessage.MessageId = $"moderation-{message.EventId:N}@mon-kado.fr";
        mimeMessage.Date = new DateTimeOffset(message.OccurredAt);
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

        return Convert
            .ToBase64String(stream.ToArray())
            .TrimEnd('=')
            .Replace(
            '+',
            '-')
            .Replace(
            '/',
            '_');
    }

    /// <summary>Classifies provider failures without copying their text or exception chain.</summary>
    /// <param name="statusCode">The optional provider status.</param>
    /// <param name="retryAfter">The optional retry delay.</param>
    /// <returns>The sanitized delivery failure.</returns>
    private static WishlistModerationEmailDeliveryException Classify(
        HttpStatusCode? statusCode,
        TimeSpan? retryAfter)
    {
        var failure = statusCode switch
        {
            HttpStatusCode.TooManyRequests => WishlistModerationEmailFailure.RateLimited,
            HttpStatusCode.RequestTimeout or null => WishlistModerationEmailFailure.Transient,
            >= HttpStatusCode.InternalServerError => WishlistModerationEmailFailure.Transient,
            _ => WishlistModerationEmailFailure.Rejected
        };

        return new WishlistModerationEmailDeliveryException(
            failure,
            retryAfter);
    }
}
