using System.Reflection;
using Catalog.Api;
using Commerce.Api.Errors;
using Commerce.Api.Security;
using Commerce.ServiceDefaults;

var builder =
    WebApplication.CreateBuilder(args);

builder.AddServiceDefaults(
    "Commerce.Api");

builder.AddInfrastructureClients(
    "Commerce.Api");

builder.Services.AddApiProblemDetails();

builder.Services.AddOpenApi();

builder.Services.AddCommerceAuthentication(
    builder.Configuration);

builder.Services.AddCatalogModule();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment() ||
    app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi();
}

app.MapCatalogModule();

app.MapGet("/", () => Results.Ok(new
{
    service = "Commerce.Api",
    status = "running",
    version =
        Assembly
            .GetExecutingAssembly()
            .GetName()
            .Version?
            .ToString() ??
        "0.0.0"
}))
.WithName("GetApiRoot")
.WithTags("System");

await app.RunAsync();

public partial class Program;
