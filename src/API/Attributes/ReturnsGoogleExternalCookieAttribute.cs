namespace JennGllg.Fr.MonKado.Back.Api.Attributes;

/// <summary>
/// Marks a response that issues the protected short-lived Google external cookie.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class ReturnsGoogleExternalCookieAttribute : Attribute;
