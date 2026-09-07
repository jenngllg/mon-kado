using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Application.Models;

/// <summary>Contains a ready notification, never an attachment or unauthenticated download link.</summary>
[ExcludeFromCodeCoverage]
public class PersonalDataExportNotification
{
    /// <summary>Gets the durable delivery identifier.</summary>
    public Guid OutboxMessageId
    {
        get; init;
    }
    /// <summary>Gets the current confirmed recipient address.</summary>
    public string RecipientAddress { get; init; } = string.Empty;
    /// <summary>Gets the trusted frontend account page.</summary>
    public Uri AccountUrl { get; init; } = new("https://localhost/profile");
    /// <summary>Gets the fixed UTC download deadline.</summary>
    public DateTime ExpiresAt
    {
        get; init;
    }
}
