using System.Text.Json.Serialization;

namespace JennGllg.Fr.MonKado.Back.Api.Contracts.Requests;

/// <summary>
/// Represents a request to update the current member profile.
/// </summary>
/// <param name="displayName">The requested display name.</param>
public class UpdateMemberProfileRequest(string? displayName)
{
    private bool? _isVisibleInMemberSearch;

    /// <summary>Gets the optional search preference; omission preserves its current value.</summary>
    public bool? IsVisibleInMemberSearch
    {
        get => _isVisibleInMemberSearch;
        init
        {
            _isVisibleInMemberSearch = value;
            HasVisibilityPreference = true;
        }
    }

    /// <summary>Gets whether the client supplied the search preference, including explicit null.</summary>
    [JsonIgnore]
    public bool HasVisibilityPreference
    {
        get;
        private set;
    }

    /// <summary>
    /// Gets the requested display name.
    /// </summary>
    public string? DisplayName { get; } = displayName;
}
