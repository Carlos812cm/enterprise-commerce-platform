using Commerce.Application.Messaging;
using Commerce.Application.Messaging.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Commerce.Application.DependencyInjection;

public static class CommandServiceCollectionExtensions
{
    public static IServiceCollection AddCommandDispatcher(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<
            ICommandDispatcher,
            CommandDispatcher>();

        services.TryAddSingleton<TimeProvider>(
            TimeProvider.System);

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped(
                typeof(ICommandBehavior<,>),
                typeof(CommandTelemetryBehavior<,>)));

        services.TryAddEnumerable(
            ServiceDescriptor.Scoped(
                typeof(ICommandBehavior<,>),
                typeof(CommandLoggingBehavior<,>)));

        return services;
    }

    public static IServiceCollection AddCommandHandler<
        TCommand,
        TResponse,
        THandler>(
        this IServiceCollection services)
        where TCommand : Command<TResponse>
        where THandler :
            class,
            ICommandHandler<TCommand, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCommandDispatcher();

        services.TryAddScoped<
            ICommandHandler<TCommand, TResponse>,
            THandler>();

        var invokerServiceType =
            typeof(ICommandInvoker<TResponse>);

        var commandKey =
            typeof(TCommand);

        var invokerAlreadyRegistered =
            services.Any(descriptor =>
                descriptor.ServiceType ==
                invokerServiceType &&
                object.Equals(
                    descriptor.ServiceKey,
                    commandKey));

        if (!invokerAlreadyRegistered)
        {
            services.AddKeyedScoped<
                ICommandInvoker<TResponse>,
                CommandInvoker<TCommand, TResponse>>(
                commandKey);
        }

        return services;
    }
}
