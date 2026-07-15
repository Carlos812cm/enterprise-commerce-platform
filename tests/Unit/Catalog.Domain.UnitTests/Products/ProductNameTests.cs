using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class ProductNameTests
{
    [Fact]
    public void CreateNormalizesWhitespace()
    {
        var result = ProductName.Create(
            "  Premium \t Running\n Shoe  ");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "Premium Running Shoe",
            result.Value.Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsMissingValue(string? value)
    {
        var result = ProductName.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Product.NameRequired",
            result.Error?.Code);
    }

    [Fact]
    public void CreateRejectsValueAboveMaximumLength()
    {
        var value = new string(
            'x',
            ProductName.MaximumLength + 1);

        var result = ProductName.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Product.NameTooLong",
            result.Error?.Code);
    }

    [Fact]
    public void CreateAcceptsValueAtMaximumLength()
    {
        var value = new string(
            'x',
            ProductName.MaximumLength);

        var result = ProductName.Create(value);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            ProductName.MaximumLength,
            result.Value.Value.Length);
    }
}
