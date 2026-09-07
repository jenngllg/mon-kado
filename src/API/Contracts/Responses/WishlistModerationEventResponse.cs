using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Responses;

/// <summary>Contains one administrator-only historical decision.</summary>
[ExcludeFromCodeCoverage]
public class WishlistModerationEventResponse
{
    /// <summary>Gets the durable decision identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the administrator identifier, or null after account deletion.</summary>
    public Guid? AdministratorId
    {
        get; init;
    }
    /// <summary>Gets the recorded decision.</summary>
    public WishlistModerationAction Action
    {
        get; init;
    }
    /// <summary>Gets the private suspension reason applicable to the decision.</summary>
    public string? Reason
    {
        get; init;
    }
    /// <summary>Gets the UTC decision date.</summary>
    public DateTime OccurredAt
    {
        get; init;
    }
}
