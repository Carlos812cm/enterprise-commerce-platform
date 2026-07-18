using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class ProductOptionDefinitionTests
{
    [Fact]
    public void DefineOptionAddsAndSortsDefinition()
    {
        var product = ProductTestData.CreateDraft();

        var sizeId = product.DefineOption(
            OptionName.Create("Size").Value,
            displayOrder: 1).Value;

        var colorId = product.DefineOption(
            OptionName.Create("Color").Value,
            displayOrder: 0).Value;

        Assert.Equal(2, product.OptionDefinitions.Count);

        Assert.Equal(
            colorId,
            product.OptionDefinitions[0].Id);

        Assert.Equal(
            sizeId,
            product.OptionDefinitions[1].Id);
    }

    [Fact]
    public void DefineOptionRejectsDuplicateName()
    {
        var product = ProductTestData.CreateDraft();

        product.DefineOption(
            OptionName.Create("Color").Value,
            displayOrder: 0);

        var result = product.DefineOption(
            OptionName.Create("COLOR").Value,
            displayOrder: 1);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.DuplicateOptionName",
            result.Error?.Code);
    }

    [Fact]
    public void DefineOptionRejectsDuplicateDisplayOrder()
    {
        var product = ProductTestData.CreateDraft();

        product.DefineOption(
            OptionName.Create("Color").Value,
            displayOrder: 0);

        var result = product.DefineOption(
            OptionName.Create("Size").Value,
            displayOrder: 0);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.DuplicateOptionDisplayOrder",
            result.Error?.Code);
    }

    [Fact]
    public void DefineOptionFailsAfterVariantExists()
    {
        var product = ProductTestData.CreateDraft();

        ProductTestData.AddVariant(
            product,
            "SKU-001");

        var result = product.DefineOption(
            OptionName.Create("Color").Value,
            displayOrder: 0);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.OptionSchemaLockedByVariants",
            result.Error?.Code);
    }

    [Fact]
    public void DefineOptionFailsAfterPublication()
    {
        var product = ProductTestData.CreateDraft();

        ProductTestData.AddVariant(
            product,
            "SKU-001");

        product.Publish(
            ProductTestData.Utc(12));

        var result = product.DefineOption(
            OptionName.Create("Color").Value,
            displayOrder: 0);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.OptionSchemaFrozen",
            result.Error?.Code);
    }
}
