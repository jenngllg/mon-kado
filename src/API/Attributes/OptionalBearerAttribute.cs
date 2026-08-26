namespace JennGllg.Fr.MonKado.Back.Api.Attributes;

/// <summary>
/// Documents an anonymous endpoint that can optionally use a valid Bearer identity.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class OptionalBearerAttribute : Attribute;
