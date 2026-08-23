using JennGllg.Fr.MonKado.Back.Application.Abstractions;
using JennGllg.Fr.MonKado.Back.Application.Commands;
using JennGllg.Fr.MonKado.Back.Application.Models;
using JennGllg.Fr.MonKado.Back.Application.Validators;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Abstractions;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Contexts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Entities;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Interceptors;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Models;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Repositories;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
/// <summary>
/// Represents infrastructure injection configuration.
/// </summary>

public static class InfrastructureInjectionConfiguration
{
    private const string ConnectionStringName = "PostgreSql";
    /// <summary>
    /// Executes the configure infrastructure injection operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The operation result.</returns>

    public static IServiceCollection ConfigureInfrastructureInjection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);

        if (string.IsNullOrWhiteSpace(connectionString))
        {

            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is required. " +
                $"Configure it with 'ConnectionStrings:{ConnectionStringName}'.");
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<AuditableEntityInterceptor>();
        services.AddDbContextPool<MonKadoDbContext>((
            provider,
            options) =>
            options
                .UseNpgsql(
                    connectionString,
                    npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(MonKadoDbContext).Assembly.FullName);
                    npgsqlOptions.MigrationsHistoryTable(
                        HistoryRepository.DefaultTableName,
                        "public");
                })
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>()));

        services
            .AddIdentityCore<MonKadoUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<MonKadoDbContext>()
            .AddDefaultTokenProviders()
            .AddPasswordValidator<MaximumPasswordLengthValidator<MonKadoUser>>();
        services.RemoveAll<ILookupNormalizer>();
        services.AddSingleton<UpperInvariantLookupNormalizer>();
        services.AddSingleton<ILookupNormalizer>(provider =>
            new InvariantFallbackLookupNormalizer(
                provider.GetRequiredService<UpperInvariantLookupNormalizer>()));
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 220_000);

        services.AddScoped<IUnitOfWork>(provider =>
            provider.GetRequiredService<MonKadoDbContext>());
        services.AddScoped<IMonKadoUserRepository, MonKadoUserRepository>();
        services.AddScoped<IMemberRepository, MemberRepository>();
        services.AddScoped<IAuthenticationEmailOutboxRepository, AuthenticationEmailOutboxRepository>();
        services.AddScoped<IAuthenticationSessionRepository, AuthenticationSessionRepository>();
        services.AddScoped<IMemberEmailChangeRequestRepository, MemberEmailChangeRequestRepository>();
        services.AddScoped<IAccountRegistrationService, AccountRegistrationService>();
        services.AddScoped<IEmailConfirmationService, EmailConfirmationService>();
        services.AddScoped<IExpiredAccountCleanup, ExpiredAccountCleanup>();
        services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
        services.AddScoped<IAccountSessionService, AccountSessionService>();
        services.AddScoped<ICurrentSessionService, CurrentSessionService>();
        services.AddScoped<IMemberProfileService, MemberProfileService>();
        services.AddScoped<IMemberEmailChangeService, MemberEmailChangeService>();
        services.AddScoped<IExpiredAuthenticationSessionCleanup, ExpiredAuthenticationSessionCleanup>();
        services.AddScoped<
            IExpiredMemberEmailChangeRequestCleanup,
            ExpiredMemberEmailChangeRequestCleanup>();

        return services;
    }
    /// <summary>
    /// Executes the configure authentication email delivery operation.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <returns>The operation result.</returns>

    public static IServiceCollection ConfigureAuthenticationEmailDelivery(this IServiceCollection services)
    {
        services.AddScoped<IAuthenticationEmailDispatcher, AuthenticationEmailDispatcher>();

        return services;
    }
}
