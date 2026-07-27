using Commerce.Application.DependencyInjection;
using Commerce.Application.Messaging;
using Commerce.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Commerce.Application.UnitTests.Messaging;

public sealed class QueryDispatcherTests
{
    [Fact]
    public async Task DispatchExecutesRegisteredHandler()
    {
        var services = CreateServices();

        services.AddQueryHandler<
            SuccessfulQuery,
            QueryResponse,
            SuccessfulQueryHandler>();

        using var serviceProvider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });

        using var scope =
            serviceProvider.CreateScope();

        var dispatcher =
            scope.ServiceProvider
                .GetRequiredService<IQueryDispatcher>();

        var result = await dispatcher.DispatchAsync(
            new SuccessfulQuery("catalog"),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("catalog-handled", result.Value.Value);
    }

    [Fact]
    public async Task DispatchExecutesBehaviorsInConfiguredOrder()
    {
        var services = CreateServices();
        var sequence = new List<string>();

        services.AddSingleton(sequence);

        services.AddScoped<
            IQueryBehavior<OrderedQuery, QueryResponse>,
            OuterBehavior>();

        services.AddScoped<
            IQueryBehavior<OrderedQuery, QueryResponse>,
            InnerBehavior>();

        services.AddQueryHandler<
            OrderedQuery,
            QueryResponse,
            OrderedQueryHandler>();

        using var serviceProvider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });

        using var scope =
            serviceProvider.CreateScope();

        var dispatcher =
            scope.ServiceProvider
                .GetRequiredService<IQueryDispatcher>();

        var result = await dispatcher.DispatchAsync(
            new OrderedQuery(),
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        Assert.Equal(
        [
            "outer:before",
            "inner:before",
            "handler",
            "inner:after",
            "outer:after"
        ],
            sequence);
    }

    [Fact]
    public async Task DispatchThrowsWhenHandlerIsNotRegistered()
    {
        var services = CreateServices();

        using var serviceProvider =
            services.BuildServiceProvider();

        using var scope =
            serviceProvider.CreateScope();

        var dispatcher =
            scope.ServiceProvider
                .GetRequiredService<IQueryDispatcher>();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dispatcher.DispatchAsync(
                    new UnregisteredQuery(),
                    CancellationToken.None));

        Assert.Contains(
            nameof(UnregisteredQuery),
            exception.Message);
    }

    [Fact]
    public async Task DispatchRejectsPreCancelledOperation()
    {
        var services = CreateServices();

        services.AddQueryHandler<
            SuccessfulQuery,
            QueryResponse,
            SuccessfulQueryHandler>();

        using var serviceProvider =
            services.BuildServiceProvider();

        using var scope =
            serviceProvider.CreateScope();

        var dispatcher =
            scope.ServiceProvider
                .GetRequiredService<IQueryDispatcher>();

        using var cancellationTokenSource =
            new CancellationTokenSource();

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => dispatcher.DispatchAsync(
                new SuccessfulQuery("catalog"),
                cancellationTokenSource.Token));
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddQueryDispatcher();

        return services;
    }

    private sealed record SuccessfulQuery(
        string Value) : Query<QueryResponse>;

    private sealed record OrderedQuery :
        Query<QueryResponse>;

    private sealed record UnregisteredQuery :
        Query<QueryResponse>;

    private sealed record QueryResponse(
        string Value);

    private sealed class SuccessfulQueryHandler :
        IQueryHandler<SuccessfulQuery, QueryResponse>
    {
        public Task<Result<QueryResponse>> HandleAsync(
            SuccessfulQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                Result.Success(
                    new QueryResponse(
                        $"{query.Value}-handled")));
        }
    }

    private sealed class OrderedQueryHandler :
        IQueryHandler<OrderedQuery, QueryResponse>
    {
        private readonly List<string> _sequence;

        public OrderedQueryHandler(
            List<string> sequence)
        {
            _sequence = sequence;
        }

        public Task<Result<QueryResponse>> HandleAsync(
            OrderedQuery query,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _sequence.Add("handler");

            return Task.FromResult(
                Result.Success(
                    new QueryResponse("handled")));
        }
    }

    private sealed class OuterBehavior :
        IQueryBehavior<OrderedQuery, QueryResponse>
    {
        private readonly List<string> _sequence;

        public OuterBehavior(
            List<string> sequence)
        {
            _sequence = sequence;
        }

        public int Order => -500;

        public async Task<Result<QueryResponse>> HandleAsync(
            OrderedQuery query,
            QueryHandlerContinuation<QueryResponse> handlerContinuation,
            CancellationToken cancellationToken)
        {
            _sequence.Add("outer:before");

            var result =
                await handlerContinuation(cancellationToken)
                .ConfigureAwait(false);

            _sequence.Add("outer:after");

            return result;
        }
    }

    private sealed class InnerBehavior :
        IQueryBehavior<OrderedQuery, QueryResponse>
    {
        private readonly List<string> _sequence;

        public InnerBehavior(
            List<string> sequence)
        {
            _sequence = sequence;
        }

        public int Order => 500;

        public async Task<Result<QueryResponse>> HandleAsync(
            OrderedQuery query,
            QueryHandlerContinuation<QueryResponse> handlerContinuation,
            CancellationToken cancellationToken)
        {
            _sequence.Add("inner:before");

            var result =
                await handlerContinuation(cancellationToken)
                .ConfigureAwait(false);

            _sequence.Add("inner:after");

            return result;
        }
    }
}
