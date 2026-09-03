using Catalog.Application.Abstractions.Persistence;
using Catalog.Application.Abstractions.Queries;
using Catalog.Infrastructure.Persistence;
using Catalog.Infrastructure.Queries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Catalog.Application.Abstractions.Caching;
using Catalog.Infrastructure.Caching;

using Catalog.Infrastructure.Messaging.RabbitMq;
using Catalog.Infrastructure.Persistence.Outbox.Processing;
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

        services.TryAddScoped<
            ICatalogUnitOfWork,
            CatalogUnitOfWork>();

        services.TryAddScoped<
            CatalogDomainEventTracker>();

        services.TryAddScoped<
            IProductDetailsReader,
            DapperProductDetailsReader>();

        services.TryAddScoped<
            IStorefrontProductSource,
            DapperStorefrontProductSource>();

        services.TryAddScoped<
            IStorefrontProductReader,
            HybridStorefrontProductReader>();

        services.TryAddSingleton<
            IStorefrontProductCacheInvalidator,
            HybridStorefrontProductCacheInvalidator>();
        return services;
    }

    public static IServiceCollection
        AddCatalogOutboxProcessing(
            this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(
            services);

        services
            .AddCatalogStorefrontCacheInvalidationBroadcaster();

        services.TryAddSingleton<
            CatalogOutboxStore>();

        services.TryAddSingleton<
            ICatalogProductPublishedPublisher,
            RabbitMqCatalogProductPublishedPublisher>();

        services.TryAddSingleton<
            CatalogOutboxDispatcher>();

        services.TryAddSingleton<
            CatalogOutboxMessageProcessor>();

        services.TryAddSingleton<
            ICatalogOutboxBatchProcessor,
            CatalogOutboxBatchRunner>();

        return services;
    }
    public static IServiceCollection
        AddCatalogStorefrontCacheInvalidationBroadcaster(
            this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(
            services);

        services.TryAddSingleton<
            IStorefrontProductCacheInvalidationBroadcaster,
            RedisStorefrontProductCacheInvalidationBroadcaster>();

        return services;
    }
    public static IServiceCollection
        AddCatalogStorefrontCacheInvalidationSubscriber(
            this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(
            services);

        services.TryAddSingleton<
            RedisStorefrontProductCacheInvalidationSubscriber>();

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IHostedService,
                StorefrontProductCacheInvalidationHostedService>());

        return services;
    }
}
