using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;
/// <summary>
/// Represents resettable authentication handler provider.
/// </summary>
/// <param name="schemeProvider">The scheme provider.</param>

public class ResettableAuthenticationHandlerProvider(
    IAuthenticationSchemeProvider schemeProvider)
    : IAuthenticationHandlerProvider, IAuthenticationHandlerResetter
{
    private readonly Dictionary<string, IAuthenticationHandler> _handlers =
        new(StringComparer.Ordinal);
    /// <summary>
    /// Executes the get handler async operation.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="authenticationScheme">The authentication scheme.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>

    public async Task<IAuthenticationHandler?> GetHandlerAsync(
        HttpContext context,
        string authenticationScheme)
    {

        if (_handlers.TryGetValue(
            authenticationScheme,
            out var handler))
            return handler;

        var scheme = await schemeProvider.GetSchemeAsync(authenticationScheme);

        if (scheme is null)
            return null;

        var instance = context.RequestServices.GetService(scheme.HandlerType)
            ?? ActivatorUtilities.CreateInstance(
                context.RequestServices,
                scheme.HandlerType);

        var authenticationHandler = (IAuthenticationHandler)instance;

        await authenticationHandler.InitializeAsync(
            scheme,
            context);
        _handlers[authenticationScheme] = authenticationHandler;

        return authenticationHandler;
    }
    /// <summary>
    /// Executes the reset operation.
    /// </summary>
    /// <param name="authenticationScheme">The authentication scheme.</param>

    public void Reset(string authenticationScheme)
    {
        _handlers.Remove(authenticationScheme);
    }
}
