using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class SkuTests
{
    [Fact]
    public void CreateNormalizesValueToUppercase()
    {
        var result = Sku.Create(
            "  run-x.blk_42  ");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "RUN-X.BLK_42",
            result.Value.Value);
    }

    [Theory]
    [InlineData("RUN X 42")]
    [InlineData("-RUN-X")]
    [InlineData("RUN-X-")]
    [InlineData("RUN/X")]
    [InlineData("CAFÉ")]
    public void CreateRejectsInvalidFormat(
        string value)
    {
        var result = Sku.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Variant.InvalidSku",
            result.Error?.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsMissingValue(string? value)
    {
        var result = Sku.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Variant.SkuRequired",
            result.Error?.Code);
    }

    [Fact]
    public void CreateRejectsValueAboveMaximumLength()
    {
        var value = new string(
            'A',
            Sku.MaximumLength + 1);

        var result = Sku.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Variant.SkuTooLong",
            result.Error?.Code);
    }
}
