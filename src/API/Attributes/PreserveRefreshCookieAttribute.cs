namespace JennGllg.Fr.MonKado.Back.Api.Attributes;

/// <summary>Preserves the existing refresh cookie even when shared JWT member validation rejects the request.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class PreserveRefreshCookieAttribute : Attribute
{
}
