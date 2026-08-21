using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;
/// <summary>
/// Represents email confirmation token provider.
/// </summary>
/// <param name="dataProtectionProvider">The data protection provider.</param>
/// <param name="options">The options.</param>
/// <param name="loggerFactory">The logger factory.</param>

public class EmailConfirmationTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> options,
    ILoggerFactory loggerFactory)
    : DataProtectorTokenProvider<TUser>(
        dataProtectionProvider,
        options,
        loggerFactory.CreateLogger<DataProtectorTokenProvider<TUser>>())
    where TUser : class;
