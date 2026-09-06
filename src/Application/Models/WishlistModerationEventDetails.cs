using JennGllg.Fr.MonKado.Back.Domain.Enums;

using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains an administrator-only historical moderation decision.</summary>
[ExcludeFromCodeCoverage]
public class WishlistModerationEventDetails
{
    /// <summary>Gets the event identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the administrator identifier, or null after deletion.</summary>
    public Guid? AdministratorId
    {
        get; init;
    }
    /// <summary>Gets the recorded decision.</summary>
    public WishlistModerationAction Action
    {
        get; init;
    }
    /// <summary>Gets the private reason applicable to the decision.</summary>
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
