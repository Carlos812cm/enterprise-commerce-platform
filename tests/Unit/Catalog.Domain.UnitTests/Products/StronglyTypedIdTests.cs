using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class StronglyTypedIdTests
{
    [Fact]
    public void ProductIdCreateNewReturnsNonEmptyIdentifier()
    {
        var productId = ProductId.CreateNew();

        Assert.False(productId.IsEmpty);
        Assert.NotEqual(Guid.Empty, productId.Value);
    }

    [Fact]
    public void ProductIdCreatePreservesIdentifier()
    {
        var value = Guid.CreateVersion7();

        var productId = ProductId.Create(value);

        Assert.Equal(value, productId.Value);
    }

    [Fact]
    public void ProductIdCreateRejectsEmptyIdentifier()
    {
        Assert.Throws<ArgumentException>(() =>
            ProductId.Create(Guid.Empty));
    }

    [Fact]
    public void ProductIdsWithSameIdentifierAreEqual()
    {
        var value = Guid.CreateVersion7();

        var first = ProductId.Create(value);
        var second = ProductId.Create(value);

        Assert.Equal(first, second);
        Assert.Equal(
            first.GetHashCode(),
            second.GetHashCode());
    }

    [Fact]
    public void DefaultProductIdIsDetectablyEmpty()
    {
        ProductId productId = default;

        Assert.True(productId.IsEmpty);
    }

    [Fact]
    public void ProductVariantIdCreateNewReturnsNonEmptyIdentifier()
    {
        var variantId = ProductVariantId.CreateNew();

        Assert.False(variantId.IsEmpty);
        Assert.NotEqual(Guid.Empty, variantId.Value);
    }

    [Fact]
    public void ProductVariantIdCreatePreservesIdentifier()
    {
        var value = Guid.CreateVersion7();

        var variantId = ProductVariantId.Create(value);

        Assert.Equal(value, variantId.Value);
    }

    [Fact]
    public void ProductVariantIdCreateRejectsEmptyIdentifier()
    {
        Assert.Throws<ArgumentException>(() =>
            ProductVariantId.Create(Guid.Empty));
    }
}
