using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class OptionNameTests
{
    [Fact]
    public void CreateNormalizesWhitespace()
    {
        var result = OptionName.Create(
            "  Storage \t Capacity  ");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "Storage Capacity",
            result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsMissingValue(string? value)
    {
        var result = OptionName.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Product.OptionNameRequired",
            result.Error?.Code);
    }

    [Fact]
    public void CreateRejectsValueAboveMaximumLength()
    {
        var value = new string(
            'x',
            OptionName.MaximumLength + 1);

        var result = OptionName.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Product.OptionNameTooLong",
            result.Error?.Code);
    }
}
