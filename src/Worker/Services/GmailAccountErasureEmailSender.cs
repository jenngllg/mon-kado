using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Worker.Exceptions;
using JennGllg.Fr.MonKado.Back.Worker.Options;

using Microsoft.Extensions.Options;

using MimeKit;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Worker.Services;

/// <summary>Sends a generic French erasure acknowledgement without account content.</summary>
/// <param name="gmailClient">The existing Gmail transport without transparent POST retries.</param>
/// <param name="options">The validated sender configuration.</param>
public class GmailAccountErasureEmailSender(
    IGmailApiClient gmailClient,
    IOptions<GmailOptions> options) : IAccountErasureEmailSender
{
    /// <inheritdoc/>
    public async Task SendAsync(
        Guid operationId,
        string recipient,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        try
        {
            var raw = await CreateRawMessageAsync(
                operationId,
                recipient,
                createdAt,
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

            throw new AccountErasureEmailDeliveryException(
                AccountErasureEmailFailure.Transient,
                null);
        }
        catch (Exception)
        {

            throw new AccountErasureEmailDeliveryException(
                AccountErasureEmailFailure.Unexpected,
                null);
        }
    }

    /// <summary>Builds a generic message with a stable Message-ID, without reintroducing erased account content.</summary>
    /// <param name="operationId">The durable operation identifier.</param>
    /// <param name="recipient">The temporarily decrypted recipient.</param>
    /// <param name="createdAt">The UTC erasure date.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The URL-safe Base64 MIME payload.</returns>
    private async Task<string> CreateRawMessageAsync(
        Guid operationId,
        string recipient,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        using var message = new MimeMessage();
        message.From.Add(new MailboxAddress(
            "MonKado",
            options.Value.SenderAddress));
        message.To.Add(MailboxAddress.Parse(recipient));
        message.Subject = "Votre compte a été supprimé – MonKado";
        message.MessageId = $"erasure-{operationId:N}@mon-kado.fr";
        message.Date = new DateTimeOffset(createdAt);
        message.Headers.Add(
            "Auto-Submitted",
            "auto-generated");
        message.Body = new TextPart("plain")
        {
            Text = "Bonjour,\n\nVotre demande d'effacement a été exécutée et votre compte MonKado a été supprimé.\n" +
                "Les images et archives devenues inaccessibles sont prises en charge par nos traitements de nettoyage. " +
                "Les sauvegardes et traces techniques suivent leurs délais de conservation applicables.\n\n" +
                "Pour toute question concernant votre demande, contactez le support par votre canal habituel.\n\nL'équipe MonKado"
        };
        await using var stream = new MemoryStream();
        await message.WriteToAsync(
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

    /// <summary>Classifies failures without copying provider text or inner exceptions.</summary>
    /// <param name="statusCode">The optional HTTP failure status.</param>
    /// <param name="retryAfter">The optional provider retry delay.</param>
    /// <returns>The sanitized delivery failure.</returns>
    private static AccountErasureEmailDeliveryException Classify(
        HttpStatusCode? statusCode,
        TimeSpan? retryAfter)
    {
        var failure = statusCode switch
        {
            HttpStatusCode.TooManyRequests => AccountErasureEmailFailure.RateLimited,
            HttpStatusCode.RequestTimeout or null => AccountErasureEmailFailure.Transient,
            >= HttpStatusCode.InternalServerError => AccountErasureEmailFailure.Transient,
            _ => AccountErasureEmailFailure.Rejected
        };

        return new AccountErasureEmailDeliveryException(
            failure,
            retryAfter);
    }
}
