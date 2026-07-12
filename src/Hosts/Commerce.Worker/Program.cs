using Commerce.ServiceDefaults;
using Commerce.Worker;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("Commerce.Worker");
builder.AddInfrastructureClients("Commerce.Worker");

builder.Services.AddHostedService<WorkerHeartbeatService>();

var app = builder.Build();

app.MapDefaultEndpoints();

await app.RunAsync();
