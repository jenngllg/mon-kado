using JennGllg.Fr.MonKado.Back.Api.Configurations;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Middleware;
using JennGllg.Fr.MonKado.Back.Application.Configurations;
using JennGllg.Fr.MonKado.Back.Domain.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Images.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

using Microsoft.IdentityModel.Logging;

IdentityModelEventSource.ShowPII = false;
IdentityModelEventSource.LogCompleteSecurityArtifact = false;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.ConfigureLocalUserSecrets(builder.Environment);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
builder.Logging.AddFilter(
    "Microsoft.AspNetCore.Hosting.Diagnostics",
    LogLevel.None);

builder.Services.ConfigureDomainInjection();
builder.Services.ConfigureApplicationInjection();
builder.Services.ConfigureImageInfrastructureInjection(builder.Configuration);
builder.Services.ConfigureInfrastructureInjection(builder.Configuration);
builder.Services.ConfigureApiInjection(
    builder.Configuration,
    builder.Environment);

var app = builder.Build();

app.UseTrustedReverseProxy();
app.UseCorrelationId();
app.UseSafeHttpRequestLogging();
app.UseApiErrorHandling();
app.UseWebSecurity();
app.UseMiddleware<GiftImageRateLimitIdentityMiddleware>();
app.UseRateLimiter();
app.UseRequestBodyLimits();
app.UseJwtAuthentication();

app.MapControllers();
app.MapApiHealthChecks();
app.MapApiOpenApi();
app.MapWebSecurity();

// The host intentionally blocks for the lifetime of the API process.
#pragma warning disable S6966
app.Run();
#pragma warning restore S6966
