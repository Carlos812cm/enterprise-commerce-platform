using Commerce.Domain;

namespace Commerce.Application.Messaging;

internal sealed class QueryInvoker<TQuery, TResponse> :
    IQueryInvoker<TResponse>
    where TQuery : Query<TResponse>
{
    private readonly IQueryHandler<TQuery, TResponse> _handler;
    private readonly IQueryBehavior<TQuery, TResponse>[] _behaviors;

    public QueryInvoker(
        IQueryHandler<TQuery, TResponse> handler,
        IEnumerable<IQueryBehavior<TQuery, TResponse>> behaviors)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(behaviors);

        _handler = handler;

        _behaviors = behaviors
            .OrderBy(static behavior => behavior.Order)
            .ToArray();
    }

    public Task<Result<TResponse>> InvokeAsync(
        Query<TResponse> query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        cancellationToken.ThrowIfCancellationRequested();

        if (query is not TQuery typedQuery)
        {
            throw new InvalidOperationException(
                $"The query invoker for '{typeof(TQuery).FullName}' received '{query.GetType().FullName}'.");
        }

        QueryHandlerContinuation<TResponse> pipeline =
            token => _handler.HandleAsync(
                typedQuery,
                token);

        for (var index = _behaviors.Length - 1;
             index >= 0;
             index--)
        {
            var behavior = _behaviors[index];
            var next = pipeline;

            pipeline = token => behavior.HandleAsync(
                typedQuery,
                next,
                token);
        }

        return pipeline(cancellationToken);
    }
}
