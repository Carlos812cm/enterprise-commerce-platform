using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class ProductAdditionalInvariantTests
{
    [Fact]
    public void DefineOptionRejectsNegativeDisplayOrder()
    {
        var product = ProductTestData.CreateDraft();

        var result = product.DefineOption(
            OptionName.Create("Color").Value,
            displayOrder: -1);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.OptionDisplayOrderInvalid",
            result.Error?.Code);
    }

    [Fact]
    public void DiscontinuedProductRejectsContentChanges()
    {
        var product = ProductTestData.CreateDraft();

        Assert.True(
            product.Discontinue(
                ProductTestData.Utc(12)).IsSuccess);

        var nameResult = product.ChangeName(
            ProductName.Create("Replacement Name").Value);

        var slugResult = product.ChangeSlug(
            ProductSlug.Create("replacement-name").Value);

        var descriptionResult =
            product.ChangeDescription(
                ProductDescription.Create(
                    "Replacement description.").Value);

        Assert.True(nameResult.IsFailure);
        Assert.True(slugResult.IsFailure);
        Assert.True(descriptionResult.IsFailure);

        Assert.Equal(
            "Catalog.Product.IsDiscontinued",
            nameResult.Error?.Code);

        Assert.Equal(
            "Catalog.Product.IsDiscontinued",
            slugResult.Error?.Code);

        Assert.Equal(
            "Catalog.Product.IsDiscontinued",
            descriptionResult.Error?.Code);
    }

    [Fact]
    public void DiscontinuedProductRejectsAddVariant()
    {
        var product = ProductTestData.CreateDraft();

        product.Discontinue(
            ProductTestData.Utc(12));

        var result = product.AddVariant(
            Sku.Create("SKU-001").Value,
            VariantOptionCombination.Empty,
            ProductTestData.Utc(13));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.IsDiscontinued",
            result.Error?.Code);
    }

    [Fact]
    public void DiscontinuedProductRejectsActivateVariant()
    {
        var product = ProductTestData.CreateDraft();

        var variantId = ProductTestData.AddVariant(
            product,
            "SKU-001");

        product.Publish(
            ProductTestData.Utc(12));

        product.Discontinue(
            ProductTestData.Utc(13));

        var result = product.ActivateVariant(
            variantId,
            ProductTestData.Utc(14));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.IsDiscontinued",
            result.Error?.Code);
    }

    [Fact]
    public void ChangeVariantSkuReturnsNotFound()
    {
        var product = ProductTestData.CreateDraft();

        var result = product.ChangeVariantSku(
            ProductVariantId.Generate(),
            Sku.Create("SKU-404").Value);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.NotFound",
            result.Error?.Code);
    }

    [Fact]
    public void ChangeVariantOptionCombinationReturnsNotFound()
    {
        var product = ProductTestData.CreateDraft();

        var result =
            product.ChangeVariantOptionCombination(
                ProductVariantId.Generate(),
                VariantOptionCombination.Empty);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.NotFound",
            result.Error?.Code);
    }

    [Fact]
    public void ActivateVariantReturnsNotFound()
    {
        var product = ProductTestData.CreateDraft();

        ProductTestData.AddVariant(
            product,
            "SKU-001");

        product.Publish(
            ProductTestData.Utc(12));

        var result = product.ActivateVariant(
            ProductVariantId.Generate(),
            ProductTestData.Utc(13));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.NotFound",
            result.Error?.Code);
    }

    [Fact]
    public void DiscontinueVariantReturnsNotFound()
    {
        var product = ProductTestData.CreateDraft();

        var result = product.DiscontinueVariant(
            ProductVariantId.Generate(),
            ProductTestData.Utc(12));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.NotFound",
            result.Error?.Code);
    }

    [Fact]
    public void ChangeVariantSkuRejectsDuplicateSku()
    {
        var setup =
            CreateDraftProductWithTwoVariants();

        var result = setup.Product.ChangeVariantSku(
            setup.WhiteVariantId,
            Sku.Create("sku-black").Value);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.DuplicateSku",
            result.Error?.Code);
    }

    [Fact]
    public void ChangeVariantOptionCombinationRejectsDuplicateCombination()
    {
        var setup =
            CreateDraftProductWithTwoVariants();

        var duplicateCombination =
            ProductTestData.CreateCombination(
                (setup.ColorId, "BLACK"));

        var result =
            setup.Product.ChangeVariantOptionCombination(
                setup.WhiteVariantId,
                duplicateCombination);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.DuplicateOptionCombination",
            result.Error?.Code);
    }

    [Fact]
    public void ActivateVariantFailsBeforePublication()
    {
        var product = ProductTestData.CreateDraft();

        var variantId = ProductTestData.AddVariant(
            product,
            "SKU-001");

        var result = product.ActivateVariant(
            variantId,
            ProductTestData.Utc(12));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.MustBePublished",
            result.Error?.Code);
    }

    private static (
        Product Product,
        ProductOptionId ColorId,
        ProductVariantId BlackVariantId,
        ProductVariantId WhiteVariantId)
        CreateDraftProductWithTwoVariants()
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

        return (
            product,
            colorId,
            blackVariantId,
            whiteVariantId);
    }
}
