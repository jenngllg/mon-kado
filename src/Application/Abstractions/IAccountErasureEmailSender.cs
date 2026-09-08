namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Sends a generic erasure acknowledgement through the configured provider.</summary>
public interface IAccountErasureEmailSender
{
    /// <summary>Makes one provider attempt without transparent retries.</summary>
    /// <param name="operationId">The stable notification identifier.</param>
    /// <param name="recipient">The confirmed recipient, held only during the send.</param>
    /// <param name="createdAt">The UTC erasure date.</param>
    /// <param name="cancellationToken">The bounded cancellation token.</param>
    /// <returns>A task completed once the provider accepts the message.</returns>
    Task SendAsync(
        Guid operationId,
        string recipient,
        DateTime createdAt,
        CancellationToken cancellationToken);
}
