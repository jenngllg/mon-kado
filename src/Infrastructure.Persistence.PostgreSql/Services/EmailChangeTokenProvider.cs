using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Options;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Services;

/// <summary>
/// Generates request-specific member email change tokens.
/// </summary>
/// <param name="dataProtectionProvider">The data protection provider.</param>
/// <param name="options">The token provider options.</param>
/// <param name="loggerFactory">The logger factory.</param>
public class EmailChangeTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailChangeTokenProviderOptions> options,
    ILoggerFactory loggerFactory)
    : DataProtectorTokenProvider<TUser>(
        dataProtectionProvider,
        options,
        loggerFactory.CreateLogger<DataProtectorTokenProvider<TUser>>())
    where TUser : class;
