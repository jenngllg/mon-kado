using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains the public identity of one searchable member.</summary>
[ExcludeFromCodeCoverage]
public class UserSearchResult
{
    /// <summary>Gets the stable member identifier.</summary>
    public Guid Id
    {
        get; init;
    }
    /// <summary>Gets the member's display name.</summary>
    public string DisplayName { get; init; } = string.Empty;
}
