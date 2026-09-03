using Microsoft.Extensions.Options;
using Catalog.Infrastructure;
using Commerce.ServiceDefaults;
using Commerce.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("Commerce.Worker");
builder.AddInfrastructureClients("Commerce.Worker");
builder.Services.AddCatalogInfrastructure();

builder.Services.AddCatalogOutboxProcessing();

builder.Services
    .AddOptions<CatalogOutboxWorkerOptions>()
    .BindConfiguration(
        CatalogOutboxWorkerOptions.SectionName)
    .Validate(
        static options =>
            options.BatchSize is >= 1 and <= 128,
        "CatalogOutbox:BatchSize must be between 1 and 128.")
    .Validate(
        static options =>
            options.LeaseDuration >=
                TimeSpan.FromSeconds(10) &&
            options.LeaseDuration <=
                TimeSpan.FromMinutes(5),
        "CatalogOutbox:LeaseDuration must be between 10 seconds and 5 minutes.")
    .Validate(
        static options =>
            options.IdleDelay >=
                TimeSpan.FromMilliseconds(100) &&
            options.IdleDelay <=
                TimeSpan.FromSeconds(30),
        "CatalogOutbox:IdleDelay must be between 100 milliseconds and 30 seconds.")
    .ValidateOnStart();

builder.Services.AddHostedService<WorkerHeartbeatService>();
builder.Services.AddHostedService<CatalogOutboxWorkerService>();

var app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync();
