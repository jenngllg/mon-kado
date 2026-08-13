using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MimeKit;

namespace JennGllg.Fr.MonKado.Back.Worker.Email;

internal static class AuthenticationEmailDeliveryExtensions
{
    public static IServiceCollection AddAuthenticationEmailWorker(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        IConfigurationSection emailSection = configuration.GetSection(AuthenticationEmailOptions.SectionName);
        AuthenticationEmailOptions email =
            emailSection.Get<AuthenticationEmailOptions>() ?? new AuthenticationEmailOptions();
        ValidateEmailOptions(email, environment);
        services.Configure<AuthenticationEmailOptions>(emailSection);

        if (email.IsEnabled)
        {
            IConfigurationSection gmailSection = configuration.GetSection(GmailOptions.SectionName);
            GmailOptions gmail = gmailSection.Get<GmailOptions>() ?? new GmailOptions();
            ValidateGmailOptions(gmail);
            services.Configure<GmailOptions>(gmailSection);
            services.AddSingleton<IGmailApiClient, GmailApiClient>();
            services.AddSingleton<IAuthenticationEmailSender, GmailAuthenticationEmailSender>();
            services.AddAuthenticationEmailDelivery();
        }

        services.AddHostedService<AuthenticationEmailDeliveryWorker>();
        return services;
    }

    private static void ValidateEmailOptions(
        AuthenticationEmailOptions options,
        IHostEnvironment environment)
    {
        bool knownProvider = options.Provider.Equals(
                AuthenticationEmailOptions.DisabledProvider,
                StringComparison.Ordinal) ||
            options.Provider.Equals(AuthenticationEmailOptions.GmailProvider, StringComparison.Ordinal);
        if (!knownProvider)
        {
            throw new InvalidOperationException("'AuthenticationEmail:Provider' must be 'Disabled' or 'Gmail'.");
        }

        if (environment.IsProduction() && !options.IsEnabled)
        {
            throw new InvalidOperationException("Authentication email delivery cannot be disabled in Production.");
        }

        if (options.IsEnabled)
        {
            ValidateFrontendOrigin(options.FrontendOrigin, environment);
        }
    }

    private static void ValidateGmailOptions(GmailOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SenderAddress) ||
            !MailboxAddress.TryParse(options.SenderAddress, out _))
        {
            throw new InvalidOperationException("'Gmail:SenderAddress' must be a valid e-mail address.");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId) ||
            string.IsNullOrWhiteSpace(options.ClientSecret) ||
            string.IsNullOrWhiteSpace(options.RefreshToken))
        {
            throw new InvalidOperationException(
                "'Gmail:ClientId', 'Gmail:ClientSecret', and 'Gmail:RefreshToken' are required.");
        }
    }

    private static void ValidateFrontendOrigin(string? origin, IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(origin) ||
            !Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            origin.EndsWith('/'))
        {
            throw new InvalidOperationException(
                "'AuthenticationEmail:FrontendOrigin' must contain only a scheme, host, and optional port.");
        }

        if (environment.IsProduction() && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("The authentication email frontend origin must use HTTPS in Production.");
        }

        if (!environment.IsProduction() && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            throw new InvalidOperationException("Plain HTTP is allowed only for a localhost frontend origin.");
        }
    }
}
