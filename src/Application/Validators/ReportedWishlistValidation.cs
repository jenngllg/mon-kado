using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Defines administrative report pagination limits.</summary>
[ExcludeFromCodeCoverage]
public static class ReportedWishlistValidation
{
    /// <summary>The default first page.</summary>
    public const int DefaultPage = 1;
    /// <summary>The default page size.</summary>
    public const int DefaultPageSize = 20;
    /// <summary>The maximum page size.</summary>
    public const int MaximumPageSize = 100;
}
