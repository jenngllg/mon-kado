using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace JennGllg.Fr.MonKado.Back.Api.UnitTests;

public class TestTicketStore : ITicketStore
{
    public Task RemoveAsync(string key)
    {

        return Task.CompletedTask;
    }

    public Task RenewAsync(
        string key,
        AuthenticationTicket ticket)
    {

        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {

        return Task.FromResult<AuthenticationTicket?>(null);
    }

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {

        return Task.FromResult("key");
    }
}
