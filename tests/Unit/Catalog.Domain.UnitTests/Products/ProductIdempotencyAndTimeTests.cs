using Catalog.Domain.Products;
using Catalog.Domain.Products.Events;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class ProductIdempotencyAndTimeTests
{
    [Fact]
    public void ActivateVariantPreservesOriginalTimestampWhenRepeated()
    {
        var product = ProductTestData.CreateDraft();

        var colorId = ProductTestData.DefineOption(
            product,
            "Color",
            displayOrder: 0);

        ProductTestData.AddVariant(
            product,
            "SKU-BLACK",
            ProductTestData.CreateCombination(
                (colorId, "Black")));

        product.Publish(
            ProductTestData.Utc(12));

        var whiteVariantId =
            ProductTestData.AddVariant(
                product,
                "SKU-WHITE",
                ProductTestData.CreateCombination(
                    (colorId, "White")),
                occurredAtHour: 13);

        product.DequeueDomainEvents();

        var firstResult = product.ActivateVariant(
            whiteVariantId,
            ProductTestData.Utc(14));

        Assert.True(firstResult.IsSuccess);

        var firstEvents =
            product.DequeueDomainEvents();

        Assert.IsType<ProductVariantActivatedDomainEvent>(
            Assert.Single(firstEvents));

        var secondResult = product.ActivateVariant(
            whiteVariantId,
            ProductTestData.Utc(15));

        Assert.True(secondResult.IsSuccess);
        Assert.Empty(product.DomainEvents);

        var variant = product.Variants.Single(
            candidate => candidate.Id == whiteVariantId);

        Assert.Equal(
            ProductTestData.Utc(14),
            variant.ActivatedAtUtc);
    }

    [Fact]
    public void DiscontinueVariantPreservesOriginalTimestampWhenRepeated()
    {
        var setup =
            CreatePublishedProductWithTwoVariants();

        setup.Product.DequeueDomainEvents();

        var firstResult =
            setup.Product.DiscontinueVariant(
                setup.BlackVariantId,
                ProductTestData.Utc(13));

        Assert.True(firstResult.IsSuccess);

        var firstEvents =
            setup.Product.DequeueDomainEvents();

        Assert.IsType<ProductVariantDiscontinuedDomainEvent>(
            Assert.Single(firstEvents));

        var secondResult =
            setup.Product.DiscontinueVariant(
                setup.BlackVariantId,
                ProductTestData.Utc(14));

        Assert.True(secondResult.IsSuccess);
        Assert.Empty(setup.Product.DomainEvents);

        var variant = setup.Product.Variants.Single(
            candidate =>
                candidate.Id == setup.BlackVariantId);

        Assert.Equal(
            ProductTestData.Utc(13),
            variant.DiscontinuedAtUtc);
    }

    [Fact]
    public void DiscontinueProductIsIdempotent()
    {
        var product =
            CreatePublishedSimpleProduct();

        var firstResult = product.Discontinue(
            ProductTestData.Utc(13));

        Assert.True(firstResult.IsSuccess);

        product.DequeueDomainEvents();

        var secondResult = product.Discontinue(
            ProductTestData.Utc(14));

        Assert.True(secondResult.IsSuccess);
        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public void DiscontinueProductPreservesOriginalTimestamp()
    {
        var product =
            CreatePublishedSimpleProduct();

        product.Discontinue(
            ProductTestData.Utc(13));

        product.Discontinue(
            ProductTestData.Utc(14));

        Assert.Equal(
            ProductTestData.Utc(13),
            product.DiscontinuedAtUtc);
    }

    [Fact]
    public void DiscontinueRejectsTimestampBeforePublicationAndActivation()
    {
        var product = ProductTestData.CreateDraft();

        ProductTestData.AddVariant(
            product,
            "SKU-001");

        product.Publish(
            ProductTestData.Utc(13));

        var result = product.Discontinue(
            ProductTestData.Utc(12));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.InvalidDiscontinuationTimestamp",
            result.Error?.Code);

        Assert.Equal(
            ProductStatus.Published,
            product.Status);
    }

    [Fact]
    public void PublishRejectsNonUtcTimestamp()
    {
        var product = ProductTestData.CreateDraft();

        ProductTestData.AddVariant(
            product,
            "SKU-001");

        var timestamp = new DateTimeOffset(
            2026,
            7,
            18,
            12,
            0,
            0,
            TimeSpan.FromHours(-6));

        Assert.Throws<ArgumentException>(() =>
            product.Publish(timestamp));
    }

    [Fact]
    public void DiscontinueRejectsDefaultTimestamp()
    {
        var product = ProductTestData.CreateDraft();

        Assert.Throws<ArgumentException>(() =>
            product.Discontinue(default));
    }

    private static Product CreatePublishedSimpleProduct()
    {
        var product = ProductTestData.CreateDraft();

        ProductTestData.AddVariant(
            product,
            "SKU-001");

        product.Publish(
            ProductTestData.Utc(12));

        return product;
    }

    private static (
        Product Product,
        ProductVariantId BlackVariantId,
        ProductVariantId WhiteVariantId)
        CreatePublishedProductWithTwoVariants()
    {
        var product = ProductTestData.CreateDraft();

        var colorId = ProductTestData.DefineOption(
            product,
            "Color",
            displayOrder: 0);

        var blackVariantId =
            ProductTestData.AddVariant(
                product,
                "SKU-BLACK",
                ProductTestData.CreateCombination(
                    (colorId, "Black")));

        var whiteVariantId =
            ProductTestData.AddVariant(
                product,
                "SKU-WHITE",
                ProductTestData.CreateCombination(
                    (colorId, "White")));

        product.Publish(
            ProductTestData.Utc(12));

        return (
            product,
            blackVariantId,
            whiteVariantId);
    }
}
