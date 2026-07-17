using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class VariantOptionCombinationTests
{
    [Fact]
    public void CreateAcceptsEmptyCombination()
    {
        var result = VariantOptionCombination.Create(
            Array.Empty<OptionSelection>());

        Assert.True(result.IsSuccess);
        Assert.Same(
            VariantOptionCombination.Empty,
            result.Value);

        Assert.True(result.Value.IsEmpty);
        Assert.Equal(0, result.Value.Count);
        Assert.Equal(
            64,
            result.Value.CanonicalKey.Length);
    }

    [Fact]
    public void CombinationIsIndependentFromInputOrder()
    {
        var color = CreateSelection(
            "00000000-0000-7000-8000-000000000001",
            "Black");

        var size = CreateSelection(
            "00000000-0000-7000-8000-000000000002",
            "42");

        var first = VariantOptionCombination.Create(
            [color, size]).Value;

        var second = VariantOptionCombination.Create(
            [size, color]).Value;

        Assert.Equal(first, second);
        Assert.Equal(
            first.CanonicalKey,
            second.CanonicalKey);
    }

    [Fact]
    public void CombinationIgnoresValueCasing()
    {
        const string optionId =
            "00000000-0000-7000-8000-000000000001";

        var first = VariantOptionCombination.Create(
            [CreateSelection(optionId, "Black")]).Value;

        var second = VariantOptionCombination.Create(
            [CreateSelection(optionId, "BLACK")]).Value;

        Assert.Equal(first, second);
        Assert.Equal(
            first.CanonicalKey,
            second.CanonicalKey);
    }

    [Fact]
    public void CombinationNormalizesUnicode()
    {
        const string optionId =
            "00000000-0000-7000-8000-000000000001";

        var first = VariantOptionCombination.Create(
            [CreateSelection(optionId, "Café")]).Value;

        var second = VariantOptionCombination.Create(
            [CreateSelection(optionId, "Cafe\u0301")]).Value;

        Assert.Equal(first, second);
        Assert.Equal(
            first.CanonicalKey,
            second.CanonicalKey);
    }

    [Fact]
    public void CreateRejectsDuplicateOptionSelections()
    {
        const string optionId =
            "00000000-0000-7000-8000-000000000001";

        var result = VariantOptionCombination.Create(
        [
            CreateSelection(optionId, "Black"),
            CreateSelection(optionId, "White")
        ]);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Variant.DuplicateOptionSelection",
            result.Error?.Code);
    }

    [Fact]
    public void CreateCopiesInputCollection()
    {
        var selections = new List<OptionSelection>
        {
            CreateSelection(
                "00000000-0000-7000-8000-000000000001",
                "Black")
        };

        var combination =
            VariantOptionCombination.Create(
                selections).Value;

        selections.Clear();

        Assert.Equal(1, combination.Count);
        Assert.Single(combination.Selections);
    }

    [Fact]
    public void CanonicalKeyHandlesDelimiterCharacters()
    {
        const string optionId =
            "00000000-0000-7000-8000-000000000001";

        var first = VariantOptionCombination.Create(
            [CreateSelection(optionId, "A|B=C")]).Value;

        var second = VariantOptionCombination.Create(
            [CreateSelection(optionId, "A|B")]).Value;

        Assert.NotEqual(first, second);
        Assert.NotEqual(
            first.CanonicalKey,
            second.CanonicalKey);
    }

    [Fact]
    public void CreateRejectsNullCollection()
    {
        Assert.Throws<ArgumentNullException>(() =>
            VariantOptionCombination.Create(null!));
    }

    private static OptionSelection CreateSelection(
        string optionId,
        string value)
    {
        return OptionSelection.Create(
            ProductOptionId.Create(
                Guid.Parse(optionId)),
            OptionValue.Create(value).Value);
    }
}
