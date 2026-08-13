using JennGllg.Fr.MonKado.Back.Application;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using JennGllg.Fr.MonKado.Back.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddPostgreSqlPersistence(builder.Configuration);
builder.Services.AddHostedService<UnconfirmedAccountCleanupWorker>();

using IHost host = builder.Build();
await host.RunAsync();
