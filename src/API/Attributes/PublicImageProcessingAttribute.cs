namespace JennGllg.Fr.MonKado.Back.Api.Attributes;

/// <summary>Marks public image rendering that must share the process-wide native memory admission limit.</summary>
[AttributeUsage(AttributeTargets.Method)]
public class PublicImageProcessingAttribute : Attribute;
