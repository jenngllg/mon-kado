namespace JennGllg.Fr.MonKado.Back.Api.Attributes;

/// <summary>
/// Marks an endpoint that returns an ETag and may require If-Match.
/// </summary>
/// <param name="isRequired">Whether the request must contain an If-Match header.</param>
[AttributeUsage(AttributeTargets.Method)]
public class EntityTagAttribute(bool isRequired = false) : Attribute
{
    /// <summary>
    /// Gets a value indicating whether the endpoint requires If-Match.
    /// </summary>
    public bool IsRequired { get; } = isRequired;
}
