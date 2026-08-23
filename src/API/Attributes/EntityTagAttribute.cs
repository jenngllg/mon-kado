namespace JennGllg.Fr.MonKado.Back.Api.Attributes;

/// <summary>
/// Marks an endpoint that uses an ETag contract.
/// </summary>
/// <param name="isRequired">Whether the request must contain an If-Match header.</param>
/// <param name="returnsEntityTag">Whether the response returns an ETag header.</param>
[AttributeUsage(AttributeTargets.Method)]
public class EntityTagAttribute(
    bool isRequired = false,
    bool returnsEntityTag = true) : Attribute
{
    /// <summary>
    /// Gets a value indicating whether the endpoint requires If-Match.
    /// </summary>
    public bool IsRequired { get; } = isRequired;

    /// <summary>
    /// Gets a value indicating whether the endpoint returns an ETag header.
    /// </summary>
    public bool ReturnsEntityTag { get; } = returnsEntityTag;
}
