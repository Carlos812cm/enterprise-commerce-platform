using Commerce.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Commerce.Application.Messaging;

internal sealed class QueryDispatcher :
    IQueryDispatcher
{
    private readonly IServiceProvider _serviceProvider;

    public QueryDispatcher(
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(
            serviceProvider);

        _serviceProvider = serviceProvider;
    }

    public Task<Result<TResponse>> DispatchAsync<TResponse>(
        Query<TResponse> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        cancellationToken.ThrowIfCancellationRequested();

        var queryType = query.GetType();

        var invoker =
            _serviceProvider
                .GetKeyedService<IQueryInvoker<TResponse>>(
                    queryType);

        if (invoker is null)
        {
            throw new InvalidOperationException(
                $"No query handler is registered for '{queryType.FullName}'.");
        }

        return invoker.InvokeAsync(
            query,
            cancellationToken);
    }
}
