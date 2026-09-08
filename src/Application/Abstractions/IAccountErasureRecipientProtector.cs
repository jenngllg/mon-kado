namespace JennGllg.Fr.MonKado.Back.Application.Abstractions;

/// <summary>Protects short-lived erasure recipients with operation-specific purposes.</summary>
public interface IAccountErasureRecipientProtector
{
    /// <summary>Protects a confirmed recipient for one erasure operation.</summary>
    /// <param name="operationId">The durable erasure identifier.</param>
    /// <param name="recipient">The confirmed recipient.</param>
    /// <returns>The protected payload.</returns>
    string Protect(
        Guid operationId,
        string recipient);
    /// <summary>Reads an operation-bound protected recipient.</summary>
    /// <param name="operationId">The durable erasure identifier.</param>
    /// <param name="protectedRecipient">The stored protected payload.</param>
    /// <returns>The recipient, or null when the payload cannot be authenticated.</returns>
    string? Read(
        Guid operationId,
        string protectedRecipient);
}
