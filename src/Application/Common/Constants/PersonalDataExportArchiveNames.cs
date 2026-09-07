namespace JennGllg.Fr.MonKado.Back.Application.Common.Constants;

/// <summary>Constructs portable archive names exclusively from trusted technical identifiers.</summary>
public static class PersonalDataExportArchiveNames
{
    /// <summary>Gets the versioned data document name.</summary>
    public const string Data = "data.json";
    /// <summary>Gets the explanatory text document name.</summary>
    public const string Readme = "README.txt";
    /// <summary>Constructs an image name without using any uploaded or user-supplied filename.</summary>
    /// <param name="wishId">The owned wish identifier, or null for the profile photo.</param>
    /// <returns>The portable relative WebP path.</returns>
    public static string GetImagePath(Guid? wishId)
    {

        return wishId.HasValue ? $"images/wishes/{wishId.Value:D}.webp" : "images/profile.webp";
    }
}
