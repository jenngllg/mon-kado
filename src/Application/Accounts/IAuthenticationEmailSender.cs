namespace JennGllg.Fr.MonKado.Back.Application.Accounts;

public interface IAuthenticationEmailSender
{
    Task<AuthenticationEmailSendResult> SendEmailConfirmationAsync(
        AuthenticationEmailMessage message,
        CancellationToken cancellationToken);
}

public sealed record AuthenticationEmailMessage(
    Guid OutboxMessageId,
    string RecipientAddress,
    Uri ConfirmationUrl);

public sealed record AuthenticationEmailSendResult(string ProviderMessageId);

public enum AuthenticationEmailFailureCategory
{
    Transient,
    RateLimited,
    Authentication,
    Permission,
    InvalidRequest,
    Unknown
}

public sealed class AuthenticationEmailDeliveryException(
    AuthenticationEmailFailureCategory category,
    TimeSpan? retryAfter = null,
    Exception? innerException = null)
    : Exception("The authentication email provider rejected or could not process the message.", innerException)
{
    public AuthenticationEmailFailureCategory Category { get; } = category;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}
