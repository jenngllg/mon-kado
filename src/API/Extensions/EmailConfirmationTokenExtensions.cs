using JennGllg.Fr.MonKado.Back.Api.Security;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;
using Microsoft.AspNetCore.Identity;

namespace JennGllg.Fr.MonKado.Back.Api.Extensions;

public static class EmailConfirmationTokenExtensions
{
    public static IServiceCollection AddEmailConfirmationTokens(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.Configure<IdentityOptions>(options =>
        {
            options.Tokens.EmailConfirmationTokenProvider =
                EmailConfirmationTokenProviderOptions.ProviderName;
            options.Tokens.ProviderMap[EmailConfirmationTokenProviderOptions.ProviderName] =
                new TokenProviderDescriptor(typeof(EmailConfirmationTokenProvider<MonKadoUser>));
        });
        services.Configure<EmailConfirmationTokenProviderOptions>(_ => { });
        services.AddTransient<EmailConfirmationTokenProvider<MonKadoUser>>();

        return services;
    }
}
