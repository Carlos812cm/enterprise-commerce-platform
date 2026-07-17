using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class ProductVariantCreationTests
{
    [Fact]
    public void CreateGeneratesDraftVariant()
    {
        var sku = Sku.Create("sku-001").Value;

        var variant = ProductVariant.Create(
            sku,
            VariantOptionCombination.Empty);

        Assert.False(variant.Id.IsEmpty);
        Assert.Equal(sku, variant.Sku);

        Assert.Same(
            VariantOptionCombination.Empty,
            variant.OptionCombination);

        Assert.Equal(
            ProductVariantStatus.Draft,
            variant.Status);

        Assert.Null(variant.ActivatedAtUtc);
        Assert.Null(variant.DiscontinuedAtUtc);
    }

    [Fact]
    public void CreatePreservesProvidedIdentifier()
    {
        var id = ProductVariantId.Generate();
        var sku = Sku.Create("sku-001").Value;

        var variant = ProductVariant.Create(
            id,
            sku,
            VariantOptionCombination.Empty);

        Assert.Equal(id, variant.Id);
    }

    [Fact]
    public void CreateRejectsEmptyIdentifier()
    {
        ProductVariantId id = default;
        var sku = Sku.Create("sku-001").Value;

        Assert.Throws<ArgumentException>(() =>
            ProductVariant.Create(
                id,
                sku,
                VariantOptionCombination.Empty));
    }

    [Fact]
    public void CreateRejectsNullSku()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ProductVariant.Create(
                null!,
                VariantOptionCombination.Empty));
    }

    [Fact]
    public void CreateRejectsNullOptionCombination()
    {
        var sku = Sku.Create("sku-001").Value;

        Assert.Throws<ArgumentNullException>(() =>
            ProductVariant.Create(
                sku,
                null!));
    }
}
