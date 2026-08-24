namespace JennGllg.Fr.MonKado.Back.Api.Attributes;

/// <summary>
/// Marks an endpoint that requires the opaque Google browser-flow binding.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class GoogleFlowBindingAttribute : Attribute;
