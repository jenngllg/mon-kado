using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Application;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddPostgreSqlPersistence(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddApiHealthChecks();
builder.Services.AddApiOpenApi();

var app = builder.Build();

app.MapControllers();
app.MapApiHealthChecks();
app.MapApiOpenApi();

app.Run();

public partial class Program;
