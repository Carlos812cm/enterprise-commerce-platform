using Catalog.Infrastructure.Persistence.Outbox;
using Catalog.Infrastructure.Persistence.Outbox.Processing;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class CatalogOutboxMessageDecoderTests
{
    private static readonly Guid ProductId =
        Guid.Parse(
            "019c28c0-31c2-7d95-b1c3-6c92e91a6155");

    private static readonly DateTimeOffset PublishedAtUtc =
        new(
            2026,
            8,
            27,
            12,
            0,
            0,
            TimeSpan.Zero);

    [Fact]
    public void DecodesStorefrontCacheInvalidation()
    {
        var message =
            CreateMessage(
                CatalogOutboxMessageTypes
                    .StorefrontProductCacheInvalidateV1,
                $$"""
                {
                  "productId": "{{ProductId}}",
                  "slug": "enterprise-monitor",
                  "publishedAtUtc": "{{PublishedAtUtc:O}}"
                }
                """);

        var result =
            CatalogOutboxMessageDecoder.Decode(
                message);

        Assert.True(
            result.Succeeded);

        Assert.Null(
            result.ErrorCode);

        var decoded =
            Assert.IsType<
                DecodedStorefrontProductCacheInvalidation>(
                    result.Message);

        Assert.Equal(
            message.Id,
            decoded.OutboxMessageId);

        Assert.Equal(
            ProductId,
            decoded.Payload.ProductId);

        Assert.Equal(
            "enterprise-monitor",
            decoded.Payload.Slug);

        Assert.Equal(
            PublishedAtUtc,
            decoded.Payload.PublishedAtUtc);
    }

    [Fact]
    public void DecodesProductPublishedIntegrationEvent()
    {
        var message =
            CreateMessage(
                CatalogOutboxMessageTypes
                    .ProductPublishedV1,
                $$"""
                {
                  "productId": "{{ProductId}}",
                  "slug": "enterprise-monitor",
                  "publishedAtUtc": "{{PublishedAtUtc:O}}"
                }
                """);

        var result =
            CatalogOutboxMessageDecoder.Decode(
                message);

        Assert.True(
            result.Succeeded);

        Assert.Null(
            result.ErrorCode);

        var decoded =
            Assert.IsType<
                DecodedProductPublished>(
                    result.Message);

        Assert.Equal(
            message.Id,
            decoded.OutboxMessageId);

        Assert.Equal(
            ProductId,
            decoded.Payload.ProductId);

        Assert.Equal(
            "enterprise-monitor",
            decoded.Payload.Slug);

        Assert.Equal(
            PublishedAtUtc,
            decoded.Payload.PublishedAtUtc);
    }

    [Fact]
    public void UnsupportedMessageTypeIsRejected()
    {
        var message =
            CreateMessage(
                "catalog.unknown.v1",
                "{}");

        var result =
            CatalogOutboxMessageDecoder.Decode(
                message);

        Assert.False(
            result.Succeeded);

        Assert.Null(
            result.Message);

        Assert.Equal(
            CatalogOutboxDecodeFailureCodes
                .UnsupportedMessageType,
            result.ErrorCode);
    }

    [Fact]
    public void MalformedJsonIsRejected()
    {
        var message =
            CreateMessage(
                CatalogOutboxMessageTypes
                    .ProductPublishedV1,
                "{ definitely-not-json");

        var result =
            CatalogOutboxMessageDecoder.Decode(
                message);

        Assert.False(
            result.Succeeded);

        Assert.Null(
            result.Message);

        Assert.Equal(
            CatalogOutboxDecodeFailureCodes
                .InvalidPayload,
            result.ErrorCode);
    }

    [Fact]
    public void SemanticallyInvalidPayloadIsRejected()
    {
        var message =
            CreateMessage(
                CatalogOutboxMessageTypes
                    .ProductPublishedV1,
                $$"""
                {
                  "productId": "{{Guid.Empty}}",
                  "slug": " ",
                  "publishedAtUtc": "{{PublishedAtUtc:O}}"
                }
                """);

        var result =
            CatalogOutboxMessageDecoder.Decode(
                message);

        Assert.False(
            result.Succeeded);

        Assert.Null(
            result.Message);

        Assert.Equal(
            CatalogOutboxDecodeFailureCodes
                .InvalidPayload,
            result.ErrorCode);
    }

    [Fact]
    public void NonCanonicalSlugIsRejected()
    {
        var message =
            CreateMessage(
                CatalogOutboxMessageTypes
                    .ProductPublishedV1,
                $$"""
                {
                  "productId": "{{ProductId}}",
                  "slug": "Enterprise--Monitor",
                  "publishedAtUtc": "{{PublishedAtUtc:O}}"
                }
                """);

        var result =
            CatalogOutboxMessageDecoder.Decode(
                message);

        Assert.False(
            result.Succeeded);

        Assert.Null(
            result.Message);

        Assert.Equal(
            CatalogOutboxDecodeFailureCodes
                .InvalidPayload,
            result.ErrorCode);
    }
    private static ClaimedCatalogOutboxMessage CreateMessage(
        string messageType,
        string payload)
    {
        return new ClaimedCatalogOutboxMessage(
            Guid.CreateVersion7(),
            messageType,
            payload,
            PublishedAtUtc,
            PublishedAtUtc,
            0,
            "decoder-test:019c28c031c27d95b1c36c92e91a6155",
            PublishedAtUtc.AddMinutes(1),
            null,
            null);
    }
}
