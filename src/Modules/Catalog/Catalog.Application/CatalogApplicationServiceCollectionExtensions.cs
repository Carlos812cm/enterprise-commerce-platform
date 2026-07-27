using Catalog.Application.Products.CreateDraftProduct;
using Catalog.Application.Products.GetProductById;
using Commerce.Application.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Catalog.Application;

public static class CatalogApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogApplication(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCommandHandler<
            CreateDraftProductCommand,
            CreateDraftProductResponse,
            CreateDraftProductCommandHandler>();

        services.AddQueryHandler<
            GetProductByIdQuery,
            AdminProductDetailsReadModel,
            GetProductByIdQueryHandler>();

        return services;
    }
}
