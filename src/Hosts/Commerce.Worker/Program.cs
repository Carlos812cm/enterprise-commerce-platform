using Commerce.ServiceDefaults;
using Commerce.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults("Commerce.Worker");

builder.Services.AddHostedService<WorkerHeartbeatService>();

var app = builder.Build();

await app.RunAsync();