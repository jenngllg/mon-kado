using Microsoft.AspNetCore.DataProtection;

using System.Security.Cryptography;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>Uses a versioned, member-specific Data Protection purpose for deletion confirmations.</summary>
/// <param name="provider">The shared application Data Protection provider.</param>
public class MemberAccountDeletionTokenService(IDataProtectionProvider provider) : IMemberAccountDeletionTokenService
{
    private const string Purpose = "MonKado.MemberAccountDeletion.v1";
    /// <inheritdoc/>
    public string Create(
        Guid memberId,
        Guid requestId)
    {
        var protector = CreateProtector(memberId);

        return protector.Protect(requestId.ToString("N"));
    }

    /// <inheritdoc/>
    public Guid? Read(
        Guid memberId,
        string token)
    {
        try
        {
            var protector = CreateProtector(memberId);
            var value = protector.Unprotect(token);

            return Guid.TryParseExact(
                value,
                "N",
                out var requestId) ? requestId : null;
        }
        catch (CryptographicException)
        {

            return null;
        }
    }

    /// <summary>Creates an isolated token purpose for the authenticated member.</summary>
    /// <param name="memberId">The member identifier.</param>
    /// <returns>The member-specific protector.</returns>
    private IDataProtector CreateProtector(Guid memberId)
    {

        return provider.CreateProtector(
            Purpose,
            memberId.ToString("N"));
    }
}
