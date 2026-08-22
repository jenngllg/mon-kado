using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RemoteIpStartupFilter(IPAddress remoteIpAddress) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return application =>
        {
            application.Use(async (
                context,
                nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = remoteIpAddress;
                await nextMiddleware(context);
            });
            next(application);
        };
    }
}
