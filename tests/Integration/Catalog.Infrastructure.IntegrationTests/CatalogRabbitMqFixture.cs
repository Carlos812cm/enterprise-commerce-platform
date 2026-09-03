using RabbitMQ.Client;
using Testcontainers.RabbitMq;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogRabbitMqFixture :
    IAsyncLifetime
{
    private const string Username =
        "commerce";

    private const string Password =
        "commerce_test_password";

    private readonly RabbitMqContainer _container =
        new RabbitMqBuilder(
            "rabbitmq:4.3-management")
            .WithUsername(
                Username)
            .WithPassword(
                Password)
            .Build();

    public async ValueTask InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task<IConnection> CreateConnectionAsync(
        CancellationToken cancellationToken)
    {
        var connectionFactory =
            new ConnectionFactory
            {
                Uri =
                    new Uri(
                        _container.GetConnectionString()),

                AutomaticRecoveryEnabled =
                    false
            };

        return await connectionFactory
            .CreateConnectionAsync(
                cancellationToken);
    }
}
