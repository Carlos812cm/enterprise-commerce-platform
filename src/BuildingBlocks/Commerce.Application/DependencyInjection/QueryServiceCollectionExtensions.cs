using Commerce.Application.Messaging;
using Commerce.Application.Messaging.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Commerce.Application.DependencyInjection;

public static class QueryServiceCollectionExtensions
{
    public static IServiceCollection AddQueryDispatcher(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<
            IQueryDispatcher,
            QueryDispatcher>();

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped(
                typeof(IQueryBehavior<,>),
                typeof(QueryTelemetryBehavior<,>)));

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped(
                typeof(IQueryBehavior<,>),
                typeof(QueryLoggingBehavior<,>)));

        return services;
    }

    public static IServiceCollection AddQueryHandler<
        TQuery,
        TResponse,
        THandler>(
        this IServiceCollection services)
        where TQuery : Query<TResponse>
        where THandler :
            class,
            IQueryHandler<TQuery, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddQueryDispatcher();

        services.TryAddScoped<
            IQueryHandler<TQuery, TResponse>,
            THandler>();

        var invokerServiceType =
            typeof(IQueryInvoker<TResponse>);

        var queryKey =
            typeof(TQuery);

        var invokerAlreadyRegistered =
            services.Any(descriptor =>
                descriptor.ServiceType ==
                invokerServiceType &&
                object.Equals(
                    descriptor.ServiceKey,
                    queryKey));

        if (!invokerAlreadyRegistered)
        {
            services.AddKeyedScoped<
                IQueryInvoker<TResponse>,
                QueryInvoker<TQuery, TResponse>>(
                queryKey);
        }

        return services;
    }
}
