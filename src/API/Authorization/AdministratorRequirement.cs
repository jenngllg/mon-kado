using Microsoft.AspNetCore.Authorization;

namespace JennGllg.Fr.MonKado.Back.Api.Authorization;

/// <summary>Requires current database-backed administrator privileges.</summary>
public class AdministratorRequirement : IAuthorizationRequirement
{
}
