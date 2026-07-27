using Commerce.Domain;

namespace Commerce.Application.Messaging;

internal interface IQueryInvoker<TResponse>
{
    Task<Result<TResponse>> InvokeAsync(
        Query<TResponse> query,
        CancellationToken cancellationToken);
}
