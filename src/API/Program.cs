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

var app = builder.Build();

app.UseWebSecurity();

app.MapControllers();
app.MapApiHealthChecks();
app.MapApiOpenApi();
app.MapWebSecurity();

await app.RunAsync();

public partial class Program
{
    protected Program() { }
}
