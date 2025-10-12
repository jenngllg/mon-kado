using JennGllg.Fr.MonKado.Back.Api.Configurations;
using JennGllg.Fr.MonKado.Back.Api.Extensions;
using JennGllg.Fr.MonKado.Back.Api.Handlers;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Configurations;
using Serilog;
using System.Text.Json.Serialization;

try
{
    var builder = WebApplication.CreateBuilder(args);
    builder.WebHost.UseUrls("http://*:7000");

    #region Services configuration

    // todo generic method
    builder.Services.BindAndValidateOptions<PostgreSqlConfiguration>(builder.Configuration);

    builder.Services.AddApiVersioning();
    builder.Services.AddControllers()
        .AddJsonOptions(options =>
        {
            var converter = new JsonStringEnumConverter();
            options.JsonSerializerOptions.Converters.Add(converter); // TODO: evaluate enum serialization strategy across layers.
        });
    builder.Services.ConfigureHealthChecks();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
    builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));
    builder.Services.ConfigurePersistenceInjection();

    #endregion

    var app = builder.Build();

    //app.UseHttpsRedirection();
    app.UseCustomHealthChecks();
    //app.UseRouting();
    //app.UseAuthorization();
    //app.MapControllers();
    app.Run();

    Log.Information("Application started successfully");
}
catch (Exception ex)
{
    Log.Error("Application failed to start : {Message}", ex.Message);
}
