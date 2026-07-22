using Commerce.Domain;

namespace Commerce.Application.Messaging;

internal interface ICommandInvoker<TResponse>
{
    Task<Result<TResponse>> InvokeAsync(
        Command<TResponse> command,
        CancellationToken cancellationToken);
}
