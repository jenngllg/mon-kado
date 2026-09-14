using System.Diagnostics.CodeAnalysis;

namespace JennGllg.Fr.MonKado.Back.Api.Attributes;

/// <summary>Includes a first-factor endpoint in the shared per-address MFA challenge-start quota.</summary>
[AttributeUsage(AttributeTargets.Method)]
[ExcludeFromCodeCoverage]
public class StartsTwoFactorChallengeAttribute : Attribute;
