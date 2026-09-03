using System.Text.Json;
using Catalog.Contracts.Products;
using Catalog.Infrastructure.Messaging.RabbitMq;
using Catalog.Infrastructure.Persistence.Outbox.Processing;
using RabbitMQ.Client;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogRabbitMqPublisherIntegrationTests :
    IClassFixture<CatalogRabbitMqFixture>
{
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

    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly CatalogRabbitMqFixture _fixture;

    public CatalogRabbitMqPublisherIntegrationTests(
        CatalogRabbitMqFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task
        RoutableMessageIsConfirmedAndContainsExpectedEnvelope()
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
                "catalog-product-published-test-",
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

            var outboxMessageId =
                Guid.CreateVersion7();

            var integrationEvent =
                new ProductPublishedIntegrationEventV1(
                    ProductId,
                    "enterprise-monitor",
                    PublishedAtUtc);

            var publishResult =
                await publisher.PublishAsync(
                    outboxMessageId,
                    integrationEvent,
                    cancellationToken);

            Assert.True(
                publishResult.Succeeded);

            Assert.Equal(
                CatalogOutboxDispatchOutcome.Success,
                publishResult.Outcome);

            Assert.Null(
                publishResult.ErrorCode);

            var delivery =
                await topologyChannel.BasicGetAsync(
                    queueName,
                    autoAck: true,
                    cancellationToken);

            Assert.NotNull(
                delivery);

            Assert.Equal(
                CatalogRabbitMqTopology
                    .IntegrationEventsExchange,
                delivery.Exchange);

            Assert.Equal(
                CatalogRabbitMqTopology
                    .ProductPublishedRoutingKey,
                delivery.RoutingKey);

            Assert.Equal(
                CatalogRabbitMqTopology.ContentType,
                delivery.BasicProperties.ContentType);

            Assert.Equal(
                CatalogRabbitMqTopology
                    .ProductPublishedRoutingKey,
                delivery.BasicProperties.Type);

            Assert.Equal(
                outboxMessageId.ToString("D"),
                delivery.BasicProperties.MessageId);

            Assert.Equal(
                DeliveryModes.Persistent,
                delivery.BasicProperties.DeliveryMode);

            Assert.True(
                delivery.BasicProperties.Persistent);

            var receivedEvent =
                JsonSerializer.Deserialize<
                    ProductPublishedIntegrationEventV1>(
                        delivery.Body.Span,
                        SerializerOptions);

            Assert.NotNull(
                receivedEvent);

            Assert.Equal(
                integrationEvent,
                receivedEvent);
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

    [Fact]
    public async Task
        MandatoryUnroutableMessageReturnsPermanentFailure()
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

        await using var publisher =
            new RabbitMqCatalogProductPublishedPublisher(
                connection);

        var result =
            await publisher.PublishAsync(
                Guid.CreateVersion7(),
                new ProductPublishedIntegrationEventV1(
                    ProductId,
                    "enterprise-monitor",
                    PublishedAtUtc),
                cancellationToken);

        Assert.False(
            result.Succeeded);

        Assert.Equal(
            CatalogOutboxDispatchOutcome.PermanentFailure,
            result.Outcome);

        Assert.Equal(
            CatalogRabbitMqPublishFailureCodes.Unroutable,
            result.ErrorCode);
    }
}
