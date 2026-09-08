using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>Confirms the target of a previously verified support erasure request.</summary>
[ExcludeFromCodeCoverage]
public class ExecuteAdministrativeAccountErasureRequest
{
    /// <summary>Gets the technical external request reference without personal data.</summary>
    public string? RequestReference
    {
        get; init;
    }
    /// <summary>Gets the account identifier explicitly confirmed by the administrator.</summary>
    public Guid? ConfirmedMemberId
    {
        get; init;
    }
}
