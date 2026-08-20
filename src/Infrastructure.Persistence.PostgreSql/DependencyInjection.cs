using JennGllg.Fr.MonKado.Back.Application.Accounts;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;

public static class DependencyInjection
{
    private const string ConnectionStringName = "PostgreSql";

    public static IServiceCollection AddPostgreSqlPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        string? connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is required. " +
                $"Configure it with 'ConnectionStrings:{ConnectionStringName}'.");
        }

        services.AddDbContextPool<MonKadoDbContext>(options =>
            options
                .UseNpgsql(connectionString, npgsqlOptions =>
                {
                    npgsqlOptions.MigrationsAssembly(typeof(MonKadoDbContext).Assembly.FullName);
                    npgsqlOptions.MigrationsHistoryTable(HistoryRepository.DefaultTableName, "public");
                    npgsqlOptions.EnableRetryOnFailure(
                        maxRetryCount: 1,
                        maxRetryDelay: TimeSpan.FromMilliseconds(500),
                        errorCodesToAdd: null);
                })
                .UseSnakeCaseNamingConvention());

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
            .AddPasswordValidator<MaximumPasswordLengthValidator<MonKadoUser>>()
            .AddSignInManager();
        services.Configure<PasswordHasherOptions>(options => options.IterationCount = 220_000);

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAccountRegistrationService, AccountRegistrationService>();
        services.AddScoped<IEmailConfirmationService, EmailConfirmationService>();
        services.AddScoped<IExpiredAccountCleanup, ExpiredAccountCleanup>();
        services.AddScoped<IAccountSessionService, AccountSessionService>();
        services.AddScoped<IExpiredAuthenticationSessionCleanup, ExpiredAuthenticationSessionCleanup>();
        services.AddSingleton<ITicketStore, PostgreSqlAuthenticationTicketStore>();
        services.AddScoped<ResettableAuthenticationHandlerProvider>();
        services.Replace(ServiceDescriptor.Scoped<IAuthenticationHandlerProvider>(provider =>
            provider.GetRequiredService<ResettableAuthenticationHandlerProvider>()));
        services.AddScoped<IAuthenticationHandlerResetter>(provider =>
            provider.GetRequiredService<ResettableAuthenticationHandlerProvider>());

        return services;
    }

    public static IServiceCollection AddAuthenticationEmailDelivery(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<IAuthenticationEmailDispatcher, AuthenticationEmailDispatcher>();
        return services;
    }
}
