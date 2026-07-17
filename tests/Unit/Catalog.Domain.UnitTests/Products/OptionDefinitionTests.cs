using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class OptionDefinitionTests
{
    [Fact]
    public void CreateNewCreatesDefinition()
    {
        var name = OptionName.Create("Color").Value;

        var result = OptionDefinition.Create(
            name,
            displayOrder: 0);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Id.IsEmpty);
        Assert.Equal(name, result.Value.Name);
        Assert.Equal(0, result.Value.DisplayOrder);
    }

    [Fact]
    public void CreatePreservesIdentifier()
    {
        var optionId = ProductOptionId.Generate();
        var name = OptionName.Create("Size").Value;

        var result = OptionDefinition.Create(
            optionId,
            name,
            displayOrder: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(optionId, result.Value.Id);
    }

    [Fact]
    public void CreateRejectsNegativeDisplayOrder()
    {
        var name = OptionName.Create("Color").Value;

        var result = OptionDefinition.Create(
            name,
            displayOrder: -1);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Product.OptionDisplayOrderInvalid",
            result.Error?.Code);
    }

    [Fact]
    public void HasSameNameIgnoresCasing()
    {
        var definitionName =
            OptionName.Create("Color").Value;

        var comparedName =
            OptionName.Create("COLOR").Value;

        var definition = OptionDefinition.Create(
            definitionName,
            displayOrder: 0).Value;

        Assert.True(
            definition.HasSameNameAs(comparedName));
    }

    [Fact]
    public void HasSameNameNormalizesUnicode()
    {
        var composedName =
            OptionName.Create("Café").Value;

        var decomposedName =
            OptionName.Create("Cafe\u0301").Value;

        var definition = OptionDefinition.Create(
            composedName,
            displayOrder: 0).Value;

        Assert.True(
            definition.HasSameNameAs(decomposedName));
    }

    [Fact]
    public void HasSameNameRejectsDifferentName()
    {
        var definitionName =
            OptionName.Create("Color").Value;

        var comparedName =
            OptionName.Create("Material").Value;

        var definition = OptionDefinition.Create(
            definitionName,
            displayOrder: 0).Value;

        Assert.False(
            definition.HasSameNameAs(comparedName));
    }
}
