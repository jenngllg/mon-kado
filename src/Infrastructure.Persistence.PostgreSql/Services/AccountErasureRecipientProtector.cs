using JennGllg.Fr.MonKado.Back.Application.Abstractions;

using Microsoft.AspNetCore.DataProtection;

using System.Security.Cryptography;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Prevents disclosure or cross-operation substitution of retained recipients.</summary>
/// <param name="provider">The shared API and Worker Data Protection key ring.</param>
public class AccountErasureRecipientProtector(IDataProtectionProvider provider) : IAccountErasureRecipientProtector
{
    private const string Purpose = "MonKado.AccountErasureRecipient.v1";

    /// <inheritdoc/>
    public string Protect(
        Guid operationId,
        string recipient)
    {

        return CreateProtector(operationId)
            .Protect(recipient);
    }

    /// <inheritdoc/>
    public string? Read(
        Guid operationId,
        string protectedRecipient)
    {
        try
        {

            return CreateProtector(operationId)
                .Unprotect(protectedRecipient);
        }
        catch (CryptographicException)
        {

            return null;
        }
    }

    /// <summary>Creates the exact purpose shared by both application processes.</summary>
    /// <param name="operationId">The durable operation identifier.</param>
    /// <returns>The operation-specific protector.</returns>
    private IDataProtector CreateProtector(Guid operationId)
    {

        return provider.CreateProtector(
            Purpose,
            operationId.ToString("D"));
    }
}
