using Commerce.Application.DependencyInjection;
using Commerce.Application.Messaging;
using Commerce.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Commerce.Application.UnitTests.Messaging;

public sealed class CommandDispatcherTests
{
    [Fact]
    public async Task DispatchExecutesRegisteredHandler()
    {
        var services = CreateServices();

        services.AddCommandHandler<
            SuccessfulCommand,
            CommandResponse,
            SuccessfulCommandHandler>();

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
                .GetRequiredService<ICommandDispatcher>();

        var result = await dispatcher.DispatchAsync(
            new SuccessfulCommand("catalog"),
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
            ICommandBehavior<OrderedCommand, CommandResponse>,
            OuterBehavior>();

        services.AddScoped<
            ICommandBehavior<OrderedCommand, CommandResponse>,
            InnerBehavior>();

        services.AddCommandHandler<
            OrderedCommand,
            CommandResponse,
            OrderedCommandHandler>();

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
                .GetRequiredService<ICommandDispatcher>();

        var result = await dispatcher.DispatchAsync(
            new OrderedCommand(),
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
                .GetRequiredService<ICommandDispatcher>();

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dispatcher.DispatchAsync(
                    new UnregisteredCommand(),
                    CancellationToken.None));

        Assert.Contains(
            nameof(UnregisteredCommand),
            exception.Message);
    }

    [Fact]
    public async Task DispatchRejectsPreCancelledOperation()
    {
        var services = CreateServices();

        services.AddCommandHandler<
            SuccessfulCommand,
            CommandResponse,
            SuccessfulCommandHandler>();

        using var serviceProvider =
            services.BuildServiceProvider();

        using var scope =
            serviceProvider.CreateScope();

        var dispatcher =
            scope.ServiceProvider
                .GetRequiredService<ICommandDispatcher>();

        using var cancellationTokenSource =
            new CancellationTokenSource();

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => dispatcher.DispatchAsync(
                new SuccessfulCommand("catalog"),
                cancellationTokenSource.Token));
    }

    private static ServiceCollection CreateServices()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddCommandDispatcher();

        return services;
    }

    private sealed record SuccessfulCommand(
        string Value) : Command<CommandResponse>;

    private sealed record OrderedCommand :
        Command<CommandResponse>;

    private sealed record UnregisteredCommand :
        Command<CommandResponse>;

    private sealed record CommandResponse(
        string Value);

    private sealed class SuccessfulCommandHandler :
        ICommandHandler<SuccessfulCommand, CommandResponse>
    {
        public Task<Result<CommandResponse>> HandleAsync(
            SuccessfulCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(
                Result.Success(
                    new CommandResponse(
                        $"{command.Value}-handled")));
        }
    }

    private sealed class OrderedCommandHandler :
        ICommandHandler<OrderedCommand, CommandResponse>
    {
        private readonly List<string> _sequence;

        public OrderedCommandHandler(
            List<string> sequence)
        {
            _sequence = sequence;
        }

        public Task<Result<CommandResponse>> HandleAsync(
            OrderedCommand command,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _sequence.Add("handler");

            return Task.FromResult(
                Result.Success(
                    new CommandResponse("handled")));
        }
    }

    private sealed class OuterBehavior :
        ICommandBehavior<OrderedCommand, CommandResponse>
    {
        private readonly List<string> _sequence;

        public OuterBehavior(
            List<string> sequence)
        {
            _sequence = sequence;
        }

        public int Order => -500;

        public async Task<Result<CommandResponse>> HandleAsync(
            OrderedCommand command,
            CommandHandlerContinuation<CommandResponse> next,
            CancellationToken cancellationToken)
        {
            _sequence.Add("outer:before");

            var result = await next(cancellationToken)
                .ConfigureAwait(false);

            _sequence.Add("outer:after");

            return result;
        }
    }

    private sealed class InnerBehavior :
        ICommandBehavior<OrderedCommand, CommandResponse>
    {
        private readonly List<string> _sequence;

        public InnerBehavior(
            List<string> sequence)
        {
            _sequence = sequence;
        }

        public int Order => 500;

        public async Task<Result<CommandResponse>> HandleAsync(
            OrderedCommand command,
            CommandHandlerContinuation<CommandResponse> next,
            CancellationToken cancellationToken)
        {
            _sequence.Add("inner:before");

            var result = await next(cancellationToken)
                .ConfigureAwait(false);

            _sequence.Add("inner:after");

            return result;
        }
    }
}
