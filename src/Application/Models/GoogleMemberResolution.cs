namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Defines how a Google identity resolved to a member after a successful commit.
/// </summary>
public enum GoogleMemberResolution
{
    /// <summary>
    /// Indicates that a new passwordless member was created.
    /// </summary>
    Created,

    /// <summary>
    /// Indicates that an existing Google login resolved the member by subject.
    /// </summary>
    Found,

    /// <summary>
    /// Indicates that Google was linked to an existing member.
    /// </summary>
    Linked
}
