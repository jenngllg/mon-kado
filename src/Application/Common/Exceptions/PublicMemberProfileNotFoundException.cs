namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Represents a public member profile that is unavailable.</summary>
public class PublicMemberProfileNotFoundException : Exception
{
    /// <summary>Initializes the same failure for absent and unconfirmed accounts.</summary>
    public PublicMemberProfileNotFoundException() : base("The member profile is unavailable.")
    {
    }
}
