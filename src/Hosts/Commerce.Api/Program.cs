using System.Reflection;
using Commerce.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults("Commerce.Api");

var app = builder.Build();

app.MapDefaultEndpoints();

app.MapGet("/", () => Results.Ok(new
{
    service = "Commerce.Api",
    status = "running",
    version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0"
}))
.WithName("GetApiRoot")
.WithTags("System");

await app.RunAsync();
