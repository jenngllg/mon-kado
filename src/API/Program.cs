using JennGllg.Fr.MonKado.Back.Api.Configurations;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application.Configurations;
using JennGllg.Fr.MonKado.Back.Domain.Configurations;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.ConfigureDomainInjection();
builder.Services.ConfigureApplicationInjection();
builder.Services.ConfigureInfrastructureInjection(builder.Configuration);
builder.Services.ConfigureApiInjection(
    builder.Configuration,
    builder.Environment);

var app = builder.Build();

app.UseTrustedReverseProxy();
app.UseCorrelationId();
app.UseApiErrorHandling();
app.UseWebSecurity();
app.UseJwtAuthentication();
app.UseRequestBodyLimits();
app.UseRateLimiter();

app.MapControllers();
app.MapApiHealthChecks();
app.MapApiOpenApi();
app.MapWebSecurity();

// The host intentionally blocks for the lifetime of the API process.
#pragma warning disable S6966
app.Run();
#pragma warning restore S6966
