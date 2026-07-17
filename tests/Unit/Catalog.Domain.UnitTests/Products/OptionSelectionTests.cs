using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class OptionSelectionTests
{
    [Fact]
    public void CreatePreservesOptionAndValue()
    {
        var optionId = ProductOptionId.Generate();
        var value = OptionValue.Create("Black").Value;

        var selection = OptionSelection.Create(
            optionId,
            value);

        Assert.Equal(optionId, selection.OptionId);
        Assert.Equal(value, selection.Value);
    }

    [Fact]
    public void SelectionsIgnoreValueCasingForEquality()
    {
        var optionId = ProductOptionId.Generate();

        var first = OptionSelection.Create(
            optionId,
            OptionValue.Create("Black").Value);

        var second = OptionSelection.Create(
            optionId,
            OptionValue.Create("BLACK").Value);

        Assert.Equal(first, second);
        Assert.Equal(
            first.GetHashCode(),
            second.GetHashCode());
    }

    [Fact]
    public void SelectionsNormalizeUnicodeForEquality()
    {
        var optionId = ProductOptionId.Generate();

        var first = OptionSelection.Create(
            optionId,
            OptionValue.Create("Café").Value);

        var second = OptionSelection.Create(
            optionId,
            OptionValue.Create("Cafe\u0301").Value);

        Assert.Equal(first, second);
    }

    [Fact]
    public void SelectionsWithDifferentOptionsAreNotEqual()
    {
        var value = OptionValue.Create("Black").Value;

        var first = OptionSelection.Create(
            ProductOptionId.Generate(),
            value);

        var second = OptionSelection.Create(
            ProductOptionId.Generate(),
            value);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void CreateRejectsEmptyOptionIdentifier()
    {
        ProductOptionId optionId = default;
        var value = OptionValue.Create("Black").Value;

        Assert.Throws<ArgumentException>(() =>
            OptionSelection.Create(
                optionId,
                value));
    }

    [Fact]
    public void CreateRejectsNullValue()
    {
        var optionId = ProductOptionId.Generate();

        Assert.Throws<ArgumentNullException>(() =>
            OptionSelection.Create(
                optionId,
                null!));
    }
}
