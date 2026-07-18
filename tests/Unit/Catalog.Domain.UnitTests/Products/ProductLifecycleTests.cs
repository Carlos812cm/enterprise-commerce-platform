using Catalog.Domain.Products;
using Catalog.Domain.Products.Events;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class ProductLifecycleTests
{
    [Fact]
    public void PublishRejectsProductWithoutDraftVariants()
    {
        var product = ProductTestData.CreateDraft();

        var result = product.Publish(
            ProductTestData.Utc(12));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.NoPublishableVariants",
            result.Error?.Code);
    }

    [Fact]
    public void PublishActivatesDraftVariants()
    {
        var product = ProductTestData.CreateDraft();

        ProductTestData.AddVariant(
            product,
            "SKU-001");

        product.DequeueDomainEvents();

        var publishedAtUtc =
            ProductTestData.Utc(12);

        var result =
            product.Publish(publishedAtUtc);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            ProductStatus.Published,
            product.Status);

        Assert.Equal(
            publishedAtUtc,
            product.PublishedAtUtc);

        var variant = Assert.Single(product.Variants);

        Assert.Equal(
            ProductVariantStatus.Active,
            variant.Status);

        Assert.Equal(
            publishedAtUtc,
            variant.ActivatedAtUtc);

        Assert.Collection(
            product.DomainEvents,
            domainEvent =>
                Assert.IsType<ProductVariantActivatedDomainEvent>(
                    domainEvent),
            domainEvent =>
                Assert.IsType<ProductPublishedDomainEvent>(
                    domainEvent));
    }

    [Fact]
    public void PublishRejectsRepeatedPublication()
    {
        var product = ProductTestData.CreateDraft();

        ProductTestData.AddVariant(
            product,
            "SKU-001");

        product.Publish(
            ProductTestData.Utc(12));

        var result = product.Publish(
            ProductTestData.Utc(13));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.AlreadyPublished",
            result.Error?.Code);
    }

    [Fact]
    public void PublishedProductCanAddAndActivateVariant()
    {
        var product = ProductTestData.CreateDraft();

        var colorId = ProductTestData.DefineOption(
            product,
            "Color",
            displayOrder: 0);

        var blackCombination =
            ProductTestData.CreateCombination(
                (colorId, "Black"));

        var whiteCombination =
            ProductTestData.CreateCombination(
                (colorId, "White"));

        ProductTestData.AddVariant(
            product,
            "SKU-BLACK",
            blackCombination);

        product.Publish(
            ProductTestData.Utc(12));

        var newVariantId =
            ProductTestData.AddVariant(
                product,
                "SKU-WHITE",
                whiteCombination,
                occurredAtHour: 13);

        var newVariant = product.Variants.Single(
            variant => variant.Id == newVariantId);

        Assert.Equal(
            ProductVariantStatus.Draft,
            newVariant.Status);

        var result = product.ActivateVariant(
            newVariantId,
            ProductTestData.Utc(14));

        Assert.True(result.IsSuccess);

        Assert.Equal(
            ProductVariantStatus.Active,
            newVariant.Status);

        Assert.Equal(
            ProductTestData.Utc(14),
            newVariant.ActivatedAtUtc);
    }

    [Fact]
    public void LastActiveVariantCannotBeDiscontinued()
    {
        var product = ProductTestData.CreateDraft();

        var variantId =
            ProductTestData.AddVariant(
                product,
                "SKU-001");

        product.Publish(
            ProductTestData.Utc(12));

        var result = product.DiscontinueVariant(
            variantId,
            ProductTestData.Utc(13));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.LastActiveVariant",
            result.Error?.Code);
    }

    [Fact]
    public void DiscontinueProductCascadesToVariants()
    {
        var product = ProductTestData.CreateDraft();

        ProductTestData.AddVariant(
            product,
            "SKU-001");

        product.Publish(
            ProductTestData.Utc(12));

        product.DequeueDomainEvents();

        var discontinuedAtUtc =
            ProductTestData.Utc(13);

        var result =
            product.Discontinue(discontinuedAtUtc);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            ProductStatus.Discontinued,
            product.Status);

        Assert.Equal(
            discontinuedAtUtc,
            product.DiscontinuedAtUtc);

        Assert.All(
            product.Variants,
            variant => Assert.Equal(
                ProductVariantStatus.Discontinued,
                variant.Status));

        Assert.Collection(
            product.DomainEvents,
            domainEvent =>
                Assert.IsType<ProductVariantDiscontinuedDomainEvent>(
                    domainEvent),
            domainEvent =>
                Assert.IsType<ProductDiscontinuedDomainEvent>(
                    domainEvent));
    }

    [Fact]
    public void PublishedSlugIsImmutableButContentCanChange()
    {
        var product = ProductTestData.CreateDraft();

        ProductTestData.AddVariant(
            product,
            "SKU-001");

        product.Publish(
            ProductTestData.Utc(12));

        var slugResult = product.ChangeSlug(
            ProductSlug.Create(
                "replacement-slug").Value);

        var nameResult = product.ChangeName(
            ProductName.Create(
                "Updated Name").Value);

        var descriptionResult =
            product.ChangeDescription(
                ProductDescription.Create(
                    "Updated description.").Value);

        Assert.True(slugResult.IsFailure);

        Assert.Equal(
            "Catalog.Product.SlugIsImmutable",
            slugResult.Error?.Code);

        Assert.True(nameResult.IsSuccess);
        Assert.True(descriptionResult.IsSuccess);
    }
}
