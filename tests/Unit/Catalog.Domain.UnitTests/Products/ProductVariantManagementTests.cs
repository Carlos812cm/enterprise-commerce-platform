using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class ProductVariantManagementTests
{
    [Fact]
    public void AddVariantAddsSimpleDraftVariant()
    {
        var product = ProductTestData.CreateDraft();

        var variantId = ProductTestData.AddVariant(
            product,
            "sku-001");

        var variant = Assert.Single(product.Variants);

        Assert.Equal(variantId, variant.Id);
        Assert.Equal("SKU-001", variant.Sku.Value);
        Assert.True(variant.OptionCombination.IsEmpty);

        Assert.Equal(
            ProductVariantStatus.Draft,
            variant.Status);
    }

    [Fact]
    public void AddVariantRejectsOptionsWhenProductHasNoDefinitions()
    {
        var product = ProductTestData.CreateDraft();

        var optionId = ProductOptionId.Generate();

        var combination =
            ProductTestData.CreateCombination(
                (optionId, "Black"));

        var result = product.AddVariant(
            Sku.Create("SKU-001").Value,
            combination,
            ProductTestData.Utc(11));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.OptionsNotDefined",
            result.Error?.Code);
    }

    [Fact]
    public void AddVariantRequiresEveryDefinedOption()
    {
        var product = ProductTestData.CreateDraft();

        var colorId =
            ProductTestData.DefineOption(
                product,
                "Color",
                0);

        ProductTestData.DefineOption(
            product,
            "Size",
            1);

        var combination =
            ProductTestData.CreateCombination(
                (colorId, "Black"));

        var result = product.AddVariant(
            Sku.Create("SKU-001").Value,
            combination,
            ProductTestData.Utc(11));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.MissingOptionSelection",
            result.Error?.Code);
    }

    [Fact]
    public void AddVariantRejectsUnknownOption()
    {
        var product = ProductTestData.CreateDraft();

        ProductTestData.DefineOption(
            product,
            "Color",
            0);

        var unknownOptionId =
            ProductOptionId.Generate();

        var combination =
            ProductTestData.CreateCombination(
                (unknownOptionId, "Black"));

        var result = product.AddVariant(
            Sku.Create("SKU-001").Value,
            combination,
            ProductTestData.Utc(11));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.UnknownOptionSelection",
            result.Error?.Code);
    }

    [Fact]
    public void AddVariantRejectsDuplicateSku()
    {
        var product = ProductTestData.CreateDraft();

        ProductTestData.AddVariant(
            product,
            "SKU-001");

        var result = product.AddVariant(
            Sku.Create("sku-001").Value,
            VariantOptionCombination.Empty,
            ProductTestData.Utc(11));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.DuplicateSku",
            result.Error?.Code);
    }

    [Fact]
    public void AddVariantRejectsDuplicateCombination()
    {
        var product = ProductTestData.CreateDraft();

        var colorId =
            ProductTestData.DefineOption(
                product,
                "Color",
                0);

        var firstCombination =
            ProductTestData.CreateCombination(
                (colorId, "Black"));

        var secondCombination =
            ProductTestData.CreateCombination(
                (colorId, "BLACK"));

        ProductTestData.AddVariant(
            product,
            "SKU-001",
            firstCombination);

        var result = product.AddVariant(
            Sku.Create("SKU-002").Value,
            secondCombination,
            ProductTestData.Utc(11));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.DuplicateOptionCombination",
            result.Error?.Code);
    }

    [Fact]
    public void DiscontinuedCombinationCanBeReintroduced()
    {
        var product = ProductTestData.CreateDraft();

        var colorId =
            ProductTestData.DefineOption(
                product,
                "Color",
                0);

        var combination =
            ProductTestData.CreateCombination(
                (colorId, "Black"));

        var originalVariantId =
            ProductTestData.AddVariant(
                product,
                "SKU-OLD",
                combination);

        product.DiscontinueVariant(
            originalVariantId,
            ProductTestData.Utc(12));

        var result = product.AddVariant(
            Sku.Create("SKU-CORRECTED").Value,
            combination,
            ProductTestData.Utc(13));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, product.Variants.Count);
    }
}
