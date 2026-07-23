using Catalog.Application.Abstractions.Persistence;
using Catalog.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Catalog.Infrastructure;

public static class CatalogInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddCatalogInfrastructure(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var contextAlreadyRegistered =
            services.Any(descriptor =>
                descriptor.ServiceType ==
                typeof(CatalogDbContext));

        if (!contextAlreadyRegistered)
        {
            services.AddDbContext<CatalogDbContext>(
                (serviceProvider, optionsBuilder) =>
                {
                    var dataSource =
                        serviceProvider
                            .GetRequiredService<NpgsqlDataSource>();

                    CatalogDbContextOptions.Configure(
                        optionsBuilder,
                        dataSource);
                });
        }

        services.TryAddScoped<
            IProductRepository,
            ProductRepository>();

        services.TryAddScoped<
            IProductSlugUniquenessChecker,
            ProductSlugUniquenessChecker>();

        services.TryAddScoped<ICatalogUnitOfWork>(
            static serviceProvider =>
                serviceProvider
                    .GetRequiredService<CatalogDbContext>());

        return services;
    }
}
