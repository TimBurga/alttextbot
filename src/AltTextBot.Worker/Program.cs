using AltTextBot.Infrastructure;
using AltTextBot.Worker.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddInfrastructure();

builder.Services.AddSingleton<WorkerHealthMonitor>();
builder.Services.AddHealthChecks()
    .AddCheck<JetstreamHealthCheck>("jetstream", tags: ["workers"])
    .AddCheck<TapHealthCheck>("tap", tags: ["workers"]);

// Hosted services in registration order
builder.Services.AddHostedService<StartupSyncService>();
builder.Services.AddHostedService<JetstreamWorker>();
builder.Services.AddHostedService<TapWorker>();
builder.Services.AddHostedService<RescoringWorker>();

var host = builder.Build();

// Apply migrations on startup
await host.ApplyMigrationsAsync();

host.Run();
