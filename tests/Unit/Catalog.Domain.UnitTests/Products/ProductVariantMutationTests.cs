using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class ProductVariantMutationTests
{
    [Fact]
    public void ChangeSkuSucceedsWhileDraft()
    {
        var variant = CreateDraftVariant();
        var updatedSku = Sku.Create("sku-002").Value;

        var result = variant.ChangeSku(updatedSku);

        Assert.True(result.IsSuccess);
        Assert.Equal(updatedSku, variant.Sku);
    }

    [Fact]
    public void ChangeSkuFailsAfterActivation()
    {
        var variant = CreateDraftVariant();

        variant.Activate(CreateUtcTimestamp());

        var result = variant.ChangeSku(
            Sku.Create("sku-002").Value);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.SkuIsImmutable",
            result.Error?.Code);
    }

    [Fact]
    public void ChangeSkuFailsAfterDiscontinuation()
    {
        var variant = CreateDraftVariant();

        variant.Discontinue(CreateUtcTimestamp());

        var result = variant.ChangeSku(
            Sku.Create("sku-002").Value);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.IsDiscontinued",
            result.Error?.Code);
    }

    [Fact]
    public void ChangeOptionCombinationSucceedsWhileDraft()
    {
        var variant = CreateDraftVariant();
        var combination = CreateCombination("Black");

        var result =
            variant.ChangeOptionCombination(combination);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            combination,
            variant.OptionCombination);
    }

    [Fact]
    public void ChangeOptionCombinationFailsAfterActivation()
    {
        var variant = CreateDraftVariant();

        variant.Activate(CreateUtcTimestamp());

        var result =
            variant.ChangeOptionCombination(
                CreateCombination("Black"));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.OptionsAreImmutable",
            result.Error?.Code);
    }

    [Fact]
    public void ChangeOptionCombinationFailsAfterDiscontinuation()
    {
        var variant = CreateDraftVariant();

        variant.Discontinue(CreateUtcTimestamp());

        var result =
            variant.ChangeOptionCombination(
                CreateCombination("Black"));

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Variant.IsDiscontinued",
            result.Error?.Code);
    }

    private static ProductVariant CreateDraftVariant()
    {
        return ProductVariant.Create(
            Sku.Create("sku-001").Value,
            VariantOptionCombination.Empty);
    }

    private static VariantOptionCombination CreateCombination(
        string value)
    {
        var optionId = ProductOptionId.Create(
            Guid.Parse(
                "00000000-0000-7000-8000-000000000001"));

        var selection = OptionSelection.Create(
            optionId,
            OptionValue.Create(value).Value);

        return VariantOptionCombination.Create(
            [selection]).Value;
    }

    private static DateTimeOffset CreateUtcTimestamp()
    {
        return new DateTimeOffset(
            2026,
            7,
            17,
            12,
            0,
            0,
            TimeSpan.Zero);
    }
}
