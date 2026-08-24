using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Worker.Options;
using JennGllg.Fr.MonKado.Back.Worker.Services;
using JennGllg.Fr.MonKado.Back.Worker.Workers;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MimeKit;

namespace JennGllg.Fr.MonKado.Back.Worker.Configurations;
/// <summary>
/// Represents authentication email delivery configuration.
/// </summary>

public static class AuthenticationEmailDeliveryConfiguration
{
    /// <summary>
    /// Executes the configure authentication email delivery operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="environment">The environment.</param>
    /// <returns>The operation result.</returns>
    public static IServiceCollection ConfigureAuthenticationEmailDelivery(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var emailSection = configuration.GetSection(AuthenticationEmailOptions.SectionName);
        var email =
            emailSection.Get<AuthenticationEmailOptions>() ?? new AuthenticationEmailOptions();
        ValidateEmailOptions(
            email,
            environment);
        ValidateDeliveryOptions(email);
        services.Configure<AuthenticationEmailOptions>(emailSection);

        if (email.IsEnabled)
        {
            var gmailSection = configuration.GetSection(GmailOptions.SectionName);
            var gmail = gmailSection.Get<GmailOptions>() ?? new GmailOptions();
            ValidateGmailOptions(gmail);
            services.Configure<GmailOptions>(gmailSection);
            services.AddSingleton<IGmailApiClient, GmailApiClient>();
            services.AddSingleton<IAuthenticationEmailSender, GmailAuthenticationEmailSender>();
            services.ConfigureAuthenticationEmailDelivery();
        }

        services.AddHostedService<AuthenticationEmailDeliveryWorker>();

        return services;
    }

    private static void ValidateEmailOptions(
        AuthenticationEmailOptions options,
        IHostEnvironment environment)
    {
        if (options.ProcessedRetentionDays is < 1 or > 365)
        {

            throw new InvalidOperationException(
                "'AuthenticationEmail:ProcessedRetentionDays' must be between 1 and 365.");
        }

        var knownProvider = options.Provider.Equals(
            AuthenticationEmailOptions.DisabledProvider,
            StringComparison.Ordinal) ||
            options.Provider.Equals(
                AuthenticationEmailOptions.GmailProvider,
                StringComparison.Ordinal);

        if (!knownProvider)
            throw new InvalidOperationException("'AuthenticationEmail:Provider' must be 'Disabled' or 'Gmail'.");

        if (environment.IsProduction() && !options.IsEnabled)
            throw new InvalidOperationException("Authentication email delivery cannot be disabled in Production.");

        if (options.IsEnabled)
            ValidateFrontendOrigin(
                options.FrontendOrigin,
                environment);
    }

    private static void ValidateGmailOptions(GmailOptions options)
    {

        if (string.IsNullOrWhiteSpace(options.SenderAddress) ||
            !MailboxAddress.TryParse(
                options.SenderAddress,
                out _))
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

        ValidateDuration(
            options.RequestTimeout,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMinutes(5),
            "Gmail:RequestTimeout");
    }

    private static void ValidateDeliveryOptions(AuthenticationEmailOptions options)
    {
        ValidateRange(
            options.DeliveryBatchSize,
            1,
            1000,
            "AuthenticationEmail:DeliveryBatchSize");
        ValidateRange(
            options.MaximumDeliveryAttempts,
            1,
            100,
            "AuthenticationEmail:MaximumDeliveryAttempts");
        ValidateDuration(
            options.DeliveryLeaseDuration,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromHours(1),
            "AuthenticationEmail:DeliveryLeaseDuration");
        ValidateDuration(
            options.PollInterval,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromHours(1),
            "AuthenticationEmail:PollInterval");
        ValidateDuration(
            options.FailureRetryInterval,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromHours(1),
            "AuthenticationEmail:FailureRetryInterval");

        TimeSpan[] retryDelays =
        [
            options.FirstRetryDelay,
            options.SecondRetryDelay,
            options.ThirdRetryDelay,
            options.FourthRetryDelay,
            options.SubsequentRetryDelay,
            options.SlowRetryDelay,
            options.MaximumRetryDelay
        ];

        foreach (var retryDelay in retryDelays)
        {
            ValidateDuration(
                retryDelay,
                TimeSpan.FromSeconds(1),
                TimeSpan.FromDays(7),
                "AuthenticationEmail retry delays");
        }

        if (options.FirstRetryDelay > options.SecondRetryDelay ||
            options.SecondRetryDelay > options.ThirdRetryDelay ||
            options.ThirdRetryDelay > options.FourthRetryDelay ||
            options.FourthRetryDelay > options.SubsequentRetryDelay)
        {
            throw new InvalidOperationException(
                "Authentication e-mail transient retry delays must be ordered from shortest to longest.");
        }

        if (options.MaximumRetryDelay < options.SubsequentRetryDelay ||
            options.MaximumRetryDelay < options.SlowRetryDelay)
        {
            throw new InvalidOperationException(
                "'AuthenticationEmail:MaximumRetryDelay' must cover every configured retry delay.");
        }
    }

    private static void ValidateRange(
        int value,
        int minimum,
        int maximum,
        string configurationKey)
    {

        if (value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"'{configurationKey}' must be between {minimum} and {maximum}.");
        }
    }

    private static void ValidateDuration(
        TimeSpan value,
        TimeSpan minimum,
        TimeSpan maximum,
        string configurationKey)
    {

        if (value < minimum || value > maximum)
        {
            throw new InvalidOperationException(
                $"'{configurationKey}' must be between {minimum} and {maximum}.");
        }
    }

    private static void ValidateFrontendOrigin(
        string? origin,
        IHostEnvironment environment)
    {

        if (string.IsNullOrWhiteSpace(origin) ||
            !Uri.TryCreate(
                origin,
                UriKind.Absolute,
                out var uri) ||
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
            throw new InvalidOperationException("The authentication email frontend origin must use HTTPS in Production.");

        if (!environment.IsProduction() && uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
            throw new InvalidOperationException("Plain HTTP is allowed only for a localhost frontend origin.");
    }
}
