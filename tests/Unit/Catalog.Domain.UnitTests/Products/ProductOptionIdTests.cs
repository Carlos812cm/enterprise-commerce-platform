using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class ProductOptionIdTests
{
    [Fact]
    public void CreateNewReturnsNonEmptyIdentifier()
    {
        var optionId = ProductOptionId.Generate();

        Assert.False(optionId.IsEmpty);
        Assert.NotEqual(Guid.Empty, optionId.Value);
    }

    [Fact]
    public void CreatePreservesIdentifier()
    {
        var value = Guid.CreateVersion7();

        var optionId = ProductOptionId.Create(value);

        Assert.Equal(value, optionId.Value);
    }

    [Fact]
    public void CreateRejectsEmptyIdentifier()
    {
        Assert.Throws<ArgumentException>(() =>
            ProductOptionId.Create(Guid.Empty));
    }

    [Fact]
    public void IdentifiersWithSameValueAreEqual()
    {
        var value = Guid.CreateVersion7();

        var first = ProductOptionId.Create(value);
        var second = ProductOptionId.Create(value);

        Assert.Equal(first, second);
        Assert.Equal(
            first.GetHashCode(),
            second.GetHashCode());
    }

    [Fact]
    public void DefaultIdentifierIsDetectablyEmpty()
    {
        ProductOptionId optionId = default;

        Assert.True(optionId.IsEmpty);
    }
}
