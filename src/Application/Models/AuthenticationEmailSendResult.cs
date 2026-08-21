using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;
/// <summary>
/// Represents authentication email send result.
/// </summary>
/// <param name="providerMessageId">The provider message id.</param>

[ExcludeFromCodeCoverage]
public class AuthenticationEmailSendResult(string providerMessageId)
{
    /// <summary>
    /// Gets provider message id.
    /// </summary>
    public string ProviderMessageId { get; } = providerMessageId;
}
