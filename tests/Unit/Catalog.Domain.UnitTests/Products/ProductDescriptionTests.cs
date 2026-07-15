using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class ProductDescriptionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateTreatsMissingValueAsEmpty(
        string? value)
    {
        var result = ProductDescription.Create(value);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsEmpty);
        Assert.Same(
            ProductDescription.Empty,
            result.Value);
    }

    [Fact]
    public void CreateNormalizesLineEndings()
    {
        var result = ProductDescription.Create(
            " First line\r\nSecond line\rThird line ");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "First line\nSecond line\nThird line",
            result.Value.Value);
    }

    [Fact]
    public void CreateRejectsValueAboveMaximumLength()
    {
        var value = new string(
            'x',
            ProductDescription.MaximumLength + 1);

        var result =
            ProductDescription.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Product.DescriptionTooLong",
            result.Error?.Code);
    }
}
