using JennGllg.Fr.MonKado.Back.Application;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql;
using JennGllg.Fr.MonKado.Back.Infrastructure.Persistence.PostgreSql.Identity;
using JennGllg.Fr.MonKado.Back.Worker;
using JennGllg.Fr.MonKado.Back.Worker.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddApplication();
builder.Services.AddMonKadoDataProtection(builder.Configuration, builder.Environment);
builder.Services.AddPostgreSqlPersistence(builder.Configuration);
builder.Services.AddAuthenticationEmailWorker(builder.Configuration, builder.Environment);
builder.Services.AddHostedService<UnconfirmedAccountCleanupWorker>();

builder.Services.AddHostedService<ExpiredAuthenticationSessionCleanupWorker>();
using IHost host = builder.Build();
await host.RunAsync();
