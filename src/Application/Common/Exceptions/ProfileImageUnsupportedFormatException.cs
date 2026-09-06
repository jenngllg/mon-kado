namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Represents unsupported profile-photo content.</summary>
public class ProfileImageUnsupportedFormatException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ProfileImageUnsupportedFormatException"/> class.</summary>
    public ProfileImageUnsupportedFormatException() : base("The supplied profile image format is not supported.")
    {
    }
}
