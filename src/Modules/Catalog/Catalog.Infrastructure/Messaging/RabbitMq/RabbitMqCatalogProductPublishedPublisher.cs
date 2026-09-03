using System.Globalization;
using System.Text.Json;
using Catalog.Contracts.Products;
using Catalog.Infrastructure.Persistence.Outbox.Processing;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Catalog.Infrastructure.Messaging.RabbitMq;

internal sealed class RabbitMqCatalogProductPublishedPublisher :
    ICatalogProductPublishedPublisher,
    IAsyncDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private static readonly CreateChannelOptions ChannelOptions =
        new(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

    private readonly IConnection _connection;

    private readonly SemaphoreSlim _channelSemaphore =
        new(
            initialCount: 1,
            maxCount: 1);

    private IChannel? _channel;
    private bool _disposed;

    public RabbitMqCatalogProductPublishedPublisher(
        IConnection connection)
    {
        ArgumentNullException.ThrowIfNull(
            connection);

        _connection = connection;
    }

    public async ValueTask<CatalogOutboxDispatchResult>
        PublishAsync(
            Guid outboxMessageId,
            ProductPublishedIntegrationEventV1 integrationEvent,
            CancellationToken cancellationToken)
    {
        if (outboxMessageId == Guid.Empty)
        {
            throw new ArgumentException(
                "The Outbox message identifier cannot be empty.",
                nameof(outboxMessageId));
        }

        ArgumentNullException.ThrowIfNull(
            integrationEvent);

        byte[] body;

        try
        {
            body =
                JsonSerializer.SerializeToUtf8Bytes(
                    integrationEvent,
                    SerializerOptions);
        }
        catch (JsonException)
        {
            return CatalogOutboxDispatchResult
                .PermanentFailure(
                    CatalogRabbitMqPublishFailureCodes
                        .SerializationFailed);
        }
        catch (NotSupportedException)
        {
            return CatalogOutboxDispatchResult
                .PermanentFailure(
                    CatalogRabbitMqPublishFailureCodes
                        .SerializationFailed);
        }

        await _channelSemaphore.WaitAsync(
            cancellationToken);

        try
        {
            ObjectDisposedException.ThrowIf(
                _disposed,
                this);

            var channel =
                await GetOrCreateChannelAsync(
                    cancellationToken);

            var properties =
                CreateProperties(
                    outboxMessageId);

            try
            {
                await channel.BasicPublishAsync(
                    exchange:
                        CatalogRabbitMqTopology
                            .IntegrationEventsExchange,
                    routingKey:
                        CatalogRabbitMqTopology
                            .ProductPublishedRoutingKey,
                    mandatory: true,
                    basicProperties: properties,
                    body: body,
                    cancellationToken:
                        cancellationToken);

                return CatalogOutboxDispatchResult.Success;
            }
            catch (PublishException exception)
                when (exception.IsReturn)
            {
                return CatalogOutboxDispatchResult
                    .PermanentFailure(
                        CatalogRabbitMqPublishFailureCodes
                            .Unroutable);
            }
            catch (PublishException)
            {
                return CatalogOutboxDispatchResult
                    .TransientFailure(
                        CatalogRabbitMqPublishFailureCodes
                            .Nacked);
            }
            catch (RabbitMQClientException)
            {
                await ResetChannelAsync();

                return CatalogOutboxDispatchResult
                    .TransientFailure(
                        CatalogRabbitMqPublishFailureCodes
                            .Unavailable);
            }
            catch (IOException)
            {
                await ResetChannelAsync();

                return CatalogOutboxDispatchResult
                    .TransientFailure(
                        CatalogRabbitMqPublishFailureCodes
                            .Unavailable);
            }
        }
        finally
        {
            _channelSemaphore.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _channelSemaphore.WaitAsync();

        try
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            await ResetChannelAsync();
        }
        finally
        {
            _channelSemaphore.Release();
        }
    }

    private async Task<IChannel> GetOrCreateChannelAsync(
        CancellationToken cancellationToken)
    {
        if (_channel is
            {
                IsOpen: true
            })
        {
            return _channel;
        }

        await ResetChannelAsync();

        var channel =
            await _connection.CreateChannelAsync(
                ChannelOptions,
                cancellationToken);

        try
        {
            await channel.ExchangeDeclareAsync(
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
        }
        catch
        {
            await channel.DisposeAsync();
            throw;
        }

        _channel = channel;

        return channel;
    }

    private static BasicProperties CreateProperties(
        Guid outboxMessageId)
    {
        return new BasicProperties
        {
            ContentType =
                CatalogRabbitMqTopology.ContentType,

            DeliveryMode =
                DeliveryModes.Persistent,

            MessageId =
                outboxMessageId.ToString(
                    "D",
                    CultureInfo.InvariantCulture),

            Type =
                CatalogRabbitMqTopology
                    .ProductPublishedRoutingKey
        };
    }

    private async ValueTask ResetChannelAsync()
    {
        var channel =
            _channel;

        _channel = null;

        if (channel is null)
        {
            return;
        }

        try
        {
            await channel.DisposeAsync();
        }
        catch (RabbitMQClientException)
        {
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }
}
