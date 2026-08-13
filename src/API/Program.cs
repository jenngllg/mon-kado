using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.AddApplication();
builder.Services.AddPostgreSqlPersistence(builder.Configuration);
builder.Services.AddControllersWithViews();
builder.Services.AddApiHealthChecks();
builder.Services.AddApiOpenApi();
builder.Services.AddWebSecurity(builder.Configuration, builder.Environment);
builder.Services.AddApiProblemDetails();
builder.Services.AddAuthenticationRateLimiting();
builder.Services.AddEmailConfirmationTokens();

var app = builder.Build();

app.UseExceptionHandler();
app.UseWebSecurity();
app.UseAuthenticationRequestBodyLimits();
app.UseRateLimiter();

app.MapControllers();
app.MapApiHealthChecks();
app.MapApiOpenApi();
app.MapWebSecurity();

// The host intentionally blocks for the lifetime of the API process.
#pragma warning disable S6966
app.Run();
#pragma warning restore S6966
