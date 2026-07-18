using Catalog.Domain.Products;
using Catalog.Domain.Products.Events;
using Xunit;

namespace Catalog.Domain.UnitTests.Products;

public sealed class ProductCreationTests
{
    [Fact]
    public void CreateDraftCreatesValidProduct()
    {
        var name =
            ProductName.Create("Enterprise Product").Value;

        var slug =
            ProductSlug.Create("enterprise-product").Value;

        var description =
            ProductDescription.Create("Description").Value;

        var occurredAtUtc =
            ProductTestData.Utc(10);

        var product = Product.CreateDraft(
            name,
            slug,
            description,
            occurredAtUtc);

        Assert.False(product.Id.IsEmpty);
        Assert.Equal(name, product.Name);
        Assert.Equal(slug, product.Slug);
        Assert.Equal(description, product.Description);

        Assert.Equal(
            ProductStatus.Draft,
            product.Status);

        Assert.Empty(product.OptionDefinitions);
        Assert.Empty(product.Variants);
        Assert.Null(product.PublishedAtUtc);
        Assert.Null(product.DiscontinuedAtUtc);

        var domainEvent =
            Assert.IsType<ProductDraftCreatedDomainEvent>(
                Assert.Single(product.DomainEvents));

        Assert.Equal(product.Id, domainEvent.ProductId);
        Assert.Equal(
            occurredAtUtc,
            domainEvent.OccurredAtUtc);
    }

    [Fact]
    public void CreateDraftRejectsNonUtcTimestamp()
    {
        var timestamp = new DateTimeOffset(
            2026,
            7,
            18,
            10,
            0,
            0,
            TimeSpan.FromHours(-6));

        Assert.Throws<ArgumentException>(() =>
            Product.CreateDraft(
                ProductName.Create("Product").Value,
                ProductSlug.Create("product-001").Value,
                ProductDescription.Empty,
                timestamp));
    }

    [Fact]
    public void DraftAllowsChangingContent()
    {
        var product = ProductTestData.CreateDraft();

        var name =
            ProductName.Create("Updated Name").Value;

        var slug =
            ProductSlug.Create("updated-name").Value;

        var description =
            ProductDescription.Create(
                "Updated description.").Value;

        Assert.True(
            product.ChangeName(name).IsSuccess);

        Assert.True(
            product.ChangeSlug(slug).IsSuccess);

        Assert.True(
            product.ChangeDescription(description).IsSuccess);

        Assert.Equal(name, product.Name);
        Assert.Equal(slug, product.Slug);
        Assert.Equal(
            description,
            product.Description);
    }
}
