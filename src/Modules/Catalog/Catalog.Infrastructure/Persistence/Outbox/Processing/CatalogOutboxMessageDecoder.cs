using System.Text.Json;
using Catalog.Contracts.Products;
using Catalog.Domain.Products;
using Catalog.Infrastructure.Persistence.Outbox;

namespace Catalog.Infrastructure.Persistence.Outbox.Processing;

internal static class CatalogOutboxMessageDecoder
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public static CatalogOutboxDecodeResult Decode(
        ClaimedCatalogOutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(
            message);

        return message.MessageType switch
        {
            CatalogOutboxMessageTypes
                .StorefrontProductCacheInvalidateV1 =>
                    DecodeStorefrontInvalidation(
                        message),

            CatalogOutboxMessageTypes
                .ProductPublishedV1 =>
                    DecodeProductPublished(
                        message),

            _ =>
                CatalogOutboxDecodeResult.Failure(
                    CatalogOutboxDecodeFailureCodes
                        .UnsupportedMessageType)
        };
    }

    private static CatalogOutboxDecodeResult
        DecodeStorefrontInvalidation(
            ClaimedCatalogOutboxMessage message)
    {
        if (string.IsNullOrWhiteSpace(
            message.Payload))
        {
            return InvalidPayload();
        }

        try
        {
            var payload =
                JsonSerializer.Deserialize<
                    StorefrontProductCacheInvalidationV1>(
                        message.Payload,
                        SerializerOptions);

            if (payload is null ||
                !IsValid(
                    payload.ProductId,
                    payload.Slug,
                    payload.PublishedAtUtc))
            {
                return InvalidPayload();
            }

            return CatalogOutboxDecodeResult.Success(
                new DecodedStorefrontProductCacheInvalidation(
                    message.Id,
                    payload));
        }
        catch (JsonException)
        {
            return InvalidPayload();
        }
        catch (NotSupportedException)
        {
            return InvalidPayload();
        }
    }

    private static CatalogOutboxDecodeResult
        DecodeProductPublished(
            ClaimedCatalogOutboxMessage message)
    {
        if (string.IsNullOrWhiteSpace(
            message.Payload))
        {
            return InvalidPayload();
        }

        try
        {
            var payload =
                JsonSerializer.Deserialize<
                    ProductPublishedIntegrationEventV1>(
                        message.Payload,
                        SerializerOptions);

            if (payload is null ||
                !IsValid(
                    payload.ProductId,
                    payload.Slug,
                    payload.PublishedAtUtc))
            {
                return InvalidPayload();
            }

            return CatalogOutboxDecodeResult.Success(
                new DecodedProductPublished(
                    message.Id,
                    payload));
        }
        catch (JsonException)
        {
            return InvalidPayload();
        }
        catch (NotSupportedException)
        {
            return InvalidPayload();
        }
    }

    private static bool IsValid(
        Guid productId,
        string? slug,
        DateTimeOffset publishedAtUtc)
    {
        return
            productId != Guid.Empty &&
            ProductSlug.Create(slug).IsSuccess &&
            publishedAtUtc != default;
    }

    private static CatalogOutboxDecodeResult InvalidPayload()
    {
        return CatalogOutboxDecodeResult.Failure(
            CatalogOutboxDecodeFailureCodes.InvalidPayload);
    }
}
