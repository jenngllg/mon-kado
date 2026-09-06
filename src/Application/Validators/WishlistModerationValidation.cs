using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Validators;

/// <summary>Defines bounded moderation input and history pagination.</summary>
[ExcludeFromCodeCoverage]
public static class WishlistModerationValidation
{
    /// <summary>Limits the private suspension reason length.</summary>
    public const int MaximumReasonLength = 1000;
    /// <summary>Defines the default one-based history page.</summary>
    public const int DefaultPage = 1;
    /// <summary>Defines the default history page size.</summary>
    public const int DefaultPageSize = 20;
    /// <summary>Limits the number of history entries returned per page.</summary>
    public const int MaximumPageSize = 100;
}
