using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

internal static class ProductTestData
{
    public static Product CreateDraft()
    {
        return Product.CreateDraft(
            ProductName.Create(
                "Enterprise Running Shoe").Value,
            ProductSlug.Create(
                "enterprise-running-shoe").Value,
            ProductDescription.Create(
                "A configurable enterprise product.").Value,
            Utc(10));
    }

    public static DateTimeOffset Utc(int hour)
    {
        return new DateTimeOffset(
            2026,
            7,
            18,
            hour,
            0,
            0,
            TimeSpan.Zero);
    }

    public static ProductOptionId DefineOption(
        Product product,
        string name,
        int displayOrder)
    {
        return product.DefineOption(
            OptionName.Create(name).Value,
            displayOrder).Value;
    }

    public static VariantOptionCombination CreateCombination(
        params (ProductOptionId OptionId, string Value)[] values)
    {
        var selections = values
            .Select(
                value => OptionSelection.Create(
                    value.OptionId,
                    OptionValue.Create(
                        value.Value).Value))
            .ToArray();

        return VariantOptionCombination.Create(
            selections).Value;
    }

    public static ProductVariantId AddVariant(
        Product product,
        string sku,
        VariantOptionCombination? combination = null,
        int occurredAtHour = 11)
    {
        var result = product.AddVariant(
            Sku.Create(sku).Value,
            combination ??
            VariantOptionCombination.Empty,
            Utc(occurredAtHour));

        Assert.True(
            result.IsSuccess,
            $"Expected variant addition to succeed, but failed with '{result.Error?.Code}': {result.Error?.Description}");

        return result.Value;
    }
}
