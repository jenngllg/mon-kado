namespace JennGllg.Fr.MonKado.Back.Api.Attributes;

/// <summary>
/// Marks an endpoint that deletes the rotating refresh token cookie.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class DeletesRefreshTokenCookieAttribute : Attribute;
