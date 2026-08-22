using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Models;

namespace JennGllg.Fr.MonKado.Back.Api.IntegrationTests;

public class ThrowingAccessTokenService : IAccessTokenService
{
    public AccessToken Create(Guid userId)
    {
        throw new InvalidOperationException("Access token creation failed.");
    }
}
