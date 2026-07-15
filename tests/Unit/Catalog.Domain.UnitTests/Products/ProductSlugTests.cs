using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class ProductSlugTests
{
    [Fact]
    public void CreateAcceptsCanonicalSlug()
    {
        var result = ProductSlug.Create(
            "  running-shoe-42  ");

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "running-shoe-42",
            result.Value.Value);
    }

    [Theory]
    [InlineData("Running-shoe")]
    [InlineData("running shoe")]
    [InlineData("running--shoe")]
    [InlineData("-running-shoe")]
    [InlineData("running-shoe-")]
    [InlineData("running_shoe")]
    [InlineData("café")]
    public void CreateRejectsNonCanonicalSlug(
        string value)
    {
        var result = ProductSlug.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Product.InvalidSlug",
            result.Error?.Code);
    }

    [Fact]
    public void CreateRejectsValueBelowMinimumLength()
    {
        var result = ProductSlug.Create("ab");

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Product.SlugTooShort",
            result.Error?.Code);
    }

    [Fact]
    public void CreateRejectsValueAboveMaximumLength()
    {
        var value = new string(
            'a',
            ProductSlug.MaximumLength + 1);

        var result = ProductSlug.Create(value);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Catalog.Product.SlugTooLong",
            result.Error?.Code);
    }
}
