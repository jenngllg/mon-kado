namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Represents an unavailable profile photo.</summary>
public class ProfileImageNotFoundException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ProfileImageNotFoundException"/> class.</summary>
    public ProfileImageNotFoundException() : base("The profile image is unavailable.")
    {
    }
}
