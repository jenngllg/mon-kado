namespace JennGllg.Fr.MonKado.Back.Application.Common.Exceptions;

/// <summary>Indicates a URL that cannot safely be fetched by the application.</summary>
public class WishImportUrlRejectedException : Exception
{
    /// <summary>Initializes an error without retaining the sensitive URL.</summary>
    public WishImportUrlRejectedException() : base("The import URL is not an allowed public HTTP destination.")
    {
    }
}
