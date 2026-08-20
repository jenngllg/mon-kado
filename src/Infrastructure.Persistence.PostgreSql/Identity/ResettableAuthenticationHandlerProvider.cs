using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;

internal interface IAuthenticationHandlerResetter
{
    void Reset(string authenticationScheme);
}

internal sealed class ResettableAuthenticationHandlerProvider(
    IAuthenticationSchemeProvider schemeProvider)
    : IAuthenticationHandlerProvider, IAuthenticationHandlerResetter
{
    private readonly Dictionary<string, IAuthenticationHandler> handlers =
        new(StringComparer.Ordinal);

    public async Task<IAuthenticationHandler?> GetHandlerAsync(
        HttpContext context,
        string authenticationScheme)
    {
        if (handlers.TryGetValue(authenticationScheme, out IAuthenticationHandler? handler))
        {
            return handler;
        }

        AuthenticationScheme? scheme = await schemeProvider.GetSchemeAsync(authenticationScheme);
        if (scheme is null)
        {
            return null;
        }

        object instance = context.RequestServices.GetService(scheme.HandlerType)
            ?? ActivatorUtilities.CreateInstance(context.RequestServices, scheme.HandlerType);
        if (instance is not IAuthenticationHandler authenticationHandler)
        {
            throw new InvalidOperationException(
                $"Authentication handler '{scheme.HandlerType}' does not implement " +
                $"{nameof(IAuthenticationHandler)}.");
        }

        await authenticationHandler.InitializeAsync(scheme, context);
        handlers[authenticationScheme] = authenticationHandler;
        return authenticationHandler;
    }

    public void Reset(string authenticationScheme) =>
        handlers.Remove(authenticationScheme);
}
