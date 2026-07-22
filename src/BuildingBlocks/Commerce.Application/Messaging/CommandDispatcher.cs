using Commerce.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Commerce.Application.Messaging;

internal sealed class CommandDispatcher :
    ICommandDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public CommandDispatcher(
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(
            serviceProvider);

        _serviceProvider = serviceProvider;
    }

    public Task<Result<TResponse>> DispatchAsync<TResponse>(
        Command<TResponse> command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        cancellationToken.ThrowIfCancellationRequested();

        var commandType = command.GetType();

        var invoker =
            _serviceProvider
                .GetKeyedService<ICommandInvoker<TResponse>>(
                    commandType);

        if (invoker is null)
        {
            throw new InvalidOperationException(
                $"No command handler is registered for '{commandType.FullName}'.");
        }

        return invoker.InvokeAsync(
            command,
            cancellationToken);
    }
}
