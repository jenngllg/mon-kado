namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Represents profile-photo content that cannot be processed safely.</summary>
public class ProfileImageInvalidException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ProfileImageInvalidException"/> class.</summary>
    public ProfileImageInvalidException() : base("The supplied profile image is corrupt or exceeds the permitted dimensions.")
    {
    }
}
