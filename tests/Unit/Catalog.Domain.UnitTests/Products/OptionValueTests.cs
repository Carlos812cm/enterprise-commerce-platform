using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class OptionValueTests
{
    [Fact]
    public void CreateNormalizesWhitespace()
    {
        var result = OptionValue.Create(
            "  Midnight \t Blue  ");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "Midnight Blue",
            result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsMissingValue(string? value)
    {
        var result = OptionValue.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Variant.OptionValueRequired",
            result.Error?.Code);
    }

    [Fact]
    public void CreateRejectsValueAboveMaximumLength()
    {
        var value = new string(
            'x',
            OptionValue.MaximumLength + 1);

        var result = OptionValue.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Variant.OptionValueTooLong",
            result.Error?.Code);
    }
}
