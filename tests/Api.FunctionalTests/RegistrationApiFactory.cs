using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using System.Net;

namespace JennGllg.Fr.MonKado.Back.Api.FunctionalTests;

public class RegistrationApiFactory(
    string environment = "Local",
    string? dataProtectionKeysPath = null,
    IPAddress? remoteIpAddress = null) : WebApplicationFactory<Program>
{
    private const string UnavailableConnectionString =
        "Host=127.0.0.1;Port=1;Database=mon_kado;Username=mon_kado;Password=functional-tests-only;" +
        "Timeout=1;Command Timeout=1;Pooling=false;SSL Mode=Disable";

    public RecordingAccountRegistrationService RegistrationService { get; } = new();

    public RecordingEmailConfirmationService EmailConfirmationService { get; } = new();

    public RecordingCurrentSessionService CurrentSessionService { get; } = new();

    public RecordingMemberProfileService MemberProfileService { get; } = new();

    public RecordingMemberEmailChangeService MemberEmailChangeService { get; } = new();

    public RecordingMemberPasswordService MemberPasswordService { get; } = new();

    public RecordingPasswordResetService PasswordResetService { get; } = new();

    public RecordingWishlistService WishlistService { get; } = new();

    public RecordingWishService WishService { get; } = new();

    public RecordingWishlistShareService WishlistShareService { get; } = new();

    public RecordingWishlistParticipantService WishlistParticipantService { get; } = new();

    public RecordingGiftReservationService GiftReservationService { get; } = new();

    public RecordingGiftReservationHistoryService GiftReservationHistoryService { get; } = new();

    public RecordingWishlistReportService WishlistReportService { get; } = new();

    public RecordingAccountSessionService SessionService { get; } = new();

    public IReadOnlyCollection<string> LogMessages => _logProvider.Messages;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var allowedOrigin = environment == "Production"
            ? "https://localhost"
            : "http://localhost:5173";
        builder.UseEnvironment(environment);
        builder.UseSetting(
            "ConnectionStrings:PostgreSql",
            UnavailableConnectionString);
        builder.UseSetting(
            "AllowedHosts",
            "localhost");
        builder.UseSetting(
            "WebSecurity:AllowedOrigins:0",
            allowedOrigin);
        builder.UseSetting(
            "WishlistSharing:FrontendOrigin",
            allowedOrigin);
        builder.UseSetting(
            "Jwt:SigningKey",
            "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=");
        builder.UseSetting(
            "ReverseProxy:KnownNetworks:0",
            "127.0.0.0/8");

        if (dataProtectionKeysPath is not null)
            builder.UseSetting(
                "DataProtection:KeysPath",
                dataProtectionKeysPath);

        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddProvider(_logProvider);
        });
        builder.ConfigureServices(services =>
        {

            if (dataProtectionKeysPath is null)
                services
                    .AddDataProtection()
                    .UseEphemeralDataProtectionProvider();

            if (remoteIpAddress is not null)
                services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(remoteIpAddress));

            services.RemoveAll<IAccountRegistrationService>();
            services.AddSingleton<IAccountRegistrationService>(RegistrationService);
            services.RemoveAll<IEmailConfirmationService>();
            services.RemoveAll<IAccountSessionService>();
            services.AddSingleton<IAccountSessionService>(SessionService);
            services.RemoveAll<ICurrentSessionService>();
            services.AddSingleton<ICurrentSessionService>(CurrentSessionService);
            services.RemoveAll<IMemberProfileService>();
            services.AddSingleton<IMemberProfileService>(MemberProfileService);
            services.RemoveAll<IMemberEmailChangeService>();
            services.AddSingleton<IMemberEmailChangeService>(MemberEmailChangeService);
            services.RemoveAll<IMemberPasswordService>();
            services.AddSingleton<IMemberPasswordService>(MemberPasswordService);
            services.RemoveAll<IPasswordResetService>();
            services.AddSingleton<IPasswordResetService>(PasswordResetService);
            services.RemoveAll<IWishlistService>();
            services.AddSingleton<IWishlistService>(WishlistService);
            services.RemoveAll<IWishService>();
            services.AddSingleton<IWishService>(WishService);
            services.RemoveAll<IWishlistShareService>();
            services.AddSingleton<IWishlistShareService>(WishlistShareService);
            services.RemoveAll<IWishlistParticipantService>();
            services.AddSingleton<IWishlistParticipantService>(WishlistParticipantService);
            services.RemoveAll<IGiftReservationService>();
            services.AddSingleton<IGiftReservationService>(GiftReservationService);
            services.RemoveAll<IGiftReservationHistoryService>();
            services.AddSingleton<IGiftReservationHistoryService>(GiftReservationHistoryService);
            services.RemoveAll<IWishlistReportService>();
            services.AddSingleton<IWishlistReportService>(WishlistReportService);
            services.AddSingleton<IEmailConfirmationService>(EmailConfirmationService);
        });
    }

    private readonly CapturingLoggerProvider _logProvider = new();
}
