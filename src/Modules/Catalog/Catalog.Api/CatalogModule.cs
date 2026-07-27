using Catalog.Api.Authorization;
using Catalog.Api.Endpoints.Products.CreateDraftProduct;
using Catalog.Application;
using Catalog.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Catalog.Api.Endpoints.Products.GetProductById;

namespace Catalog.Api;

public static class CatalogModule
{
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCatalogApplication();
        services.AddCatalogInfrastructure();

        services
            .AddAuthorizationBuilder()
            .AddPolicy(
                CatalogAuthorization
                    .ManageProductsPolicy,
                policy =>
                {
                    policy.RequireAuthenticatedUser();

                    policy.RequireClaim(
                        CatalogAuthorization
                            .PermissionClaim,
                        CatalogAuthorization
                            .ProductsWritePermission);
                });

        return services;
    }

    public static IEndpointRouteBuilder MapCatalogModule(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var catalogGroup =
            endpoints
                .MapGroup("/api/catalog")
                .WithTags("Catalog");

        catalogGroup.MapCreateDraftProduct();
        catalogGroup.MapGetProductById();

        return endpoints;
    }
}
