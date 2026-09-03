using Catalog.Contracts.Products;
using Catalog.Infrastructure.Messaging.RabbitMq;
using RabbitMQ.Client;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class
    CatalogRabbitMqPublisherConcurrencyIntegrationTests :
    IClassFixture<CatalogRabbitMqFixture>
{
    private const int PublishCount = 16;

    private static readonly Guid ProductId =
        Guid.Parse(
            "019c28c0-31c2-7d95-b1c3-6c92e91a6155");

    private static readonly DateTimeOffset PublishedAtUtc =
        new(
            2026,
            8,
            28,
            12,
            0,
            0,
            TimeSpan.Zero);

    private readonly CatalogRabbitMqFixture _fixture;

    public CatalogRabbitMqPublisherConcurrencyIntegrationTests(
        CatalogRabbitMqFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        ConcurrentPublishesOnSinglePublisherAreConfirmed()
    {
        var cancellationToken =
            TestContext.Current.CancellationToken;

        await using var connection =
            await _fixture.CreateConnectionAsync(
                cancellationToken);

        await using var topologyChannel =
            await connection.CreateChannelAsync(
                cancellationToken:
                    cancellationToken);

        await topologyChannel.ExchangeDeclareAsync(
            exchange:
                CatalogRabbitMqTopology
                    .IntegrationEventsExchange,
            type:
                ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            noWait: false,
            cancellationToken:
                cancellationToken);

        var queueName =
            string.Concat(
                "catalog-concurrent-publisher-test-",
                Guid.CreateVersion7()
                    .ToString("N"));

        await topologyChannel.QueueDeclareAsync(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            passive: false,
            noWait: false,
            cancellationToken:
                cancellationToken);

        try
        {
            await topologyChannel.QueueBindAsync(
                queue:
                    queueName,
                exchange:
                    CatalogRabbitMqTopology
                        .IntegrationEventsExchange,
                routingKey:
                    CatalogRabbitMqTopology
                        .ProductPublishedRoutingKey,
                arguments: null,
                noWait: false,
                cancellationToken:
                    cancellationToken);

            await using var publisher =
                new RabbitMqCatalogProductPublishedPublisher(
                    connection);

            var outboxMessageIds =
                Enumerable
                    .Range(
                        0,
                        PublishCount)
                    .Select(
                        static _ =>
                            Guid.CreateVersion7())
                    .ToArray();

            var integrationEvent =
                new ProductPublishedIntegrationEventV1(
                    ProductId,
                    "enterprise-monitor",
                    PublishedAtUtc);

            var publishTasks =
                outboxMessageIds
                    .Select(
                        messageId =>
                            publisher
                                .PublishAsync(
                                    messageId,
                                    integrationEvent,
                                    cancellationToken)
                                .AsTask())
                    .ToArray();

            var results =
                await Task.WhenAll(
                    publishTasks);

            Assert.Equal(
                PublishCount,
                results.Length);

            Assert.All(
                results,
                static result =>
                {
                    Assert.True(
                        result.Succeeded);
                });

            var receivedMessageIds =
                new HashSet<Guid>();

            for (var index = 0;
                index < PublishCount;
                index++)
            {
                var delivery =
                    await topologyChannel.BasicGetAsync(
                        queueName,
                        autoAck: true,
                        cancellationToken);

                Assert.NotNull(
                    delivery);

                Assert.Equal(
                    CatalogRabbitMqTopology
                        .ProductPublishedRoutingKey,
                    delivery.RoutingKey);

                Assert.True(
                    Guid.TryParse(
                        delivery.BasicProperties.MessageId,
                        out var messageId));

                Assert.True(
                    receivedMessageIds.Add(
                        messageId));
            }

            Assert.Equal(
                PublishCount,
                receivedMessageIds.Count);

            Assert.True(
                outboxMessageIds
                    .ToHashSet()
                    .SetEquals(
                        receivedMessageIds));

            var extraDelivery =
                await topologyChannel.BasicGetAsync(
                    queueName,
                    autoAck: true,
                    cancellationToken);

            Assert.Null(
                extraDelivery);
        }
        finally
        {
            await topologyChannel.QueueDeleteAsync(
                queue:
                    queueName,
                ifUnused: false,
                ifEmpty: false,
                noWait: false,
                cancellationToken:
                    cancellationToken);
        }
    }
}
