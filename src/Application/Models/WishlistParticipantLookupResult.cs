using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>
/// Represents current-participant resolution without exposing guest credentials.
/// </summary>
/// <param name="outcome">The resolution outcome.</param>
/// <param name="participant">The optional current participant.</param>
[ExcludeFromCodeCoverage]
public class WishlistParticipantLookupResult(
    WishlistParticipantLookupOutcome outcome,
    WishlistParticipantDetails? participant)
{
    /// <summary>Gets the resolution outcome.</summary>
    public WishlistParticipantLookupOutcome Outcome { get; } = outcome;

    /// <summary>Gets the current participant when found.</summary>
    public WishlistParticipantDetails? Participant { get; } = participant;
}
