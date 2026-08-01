using Catalog.Application.Abstractions.Queries;
using Catalog.Application.Products.GetPublishedProductBySlug;
using Catalog.Domain.Products;
using Xunit;

namespace Catalog.Application.UnitTests.Products.GetPublishedProductBySlug;

public sealed class
    GetPublishedProductBySlugQueryHandlerTests
{
    [Fact]
    public async Task HandleReturnsPublishedProduct()
    {
        var expected =
            CreateReadModel(
                "enterprise-keyboard");

        var reader =
            new StubStorefrontProductReader
            {
                ProductToReturn = expected
            };

        var handler =
            new GetPublishedProductBySlugQueryHandler(
                reader);

        var result = await handler.HandleAsync(
            new GetPublishedProductBySlugQuery(
                "enterprise-keyboard"),
            TestContext.Current
                .CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Same(expected, result.Value);
        Assert.Equal(1, reader.CallCount);
    }

    [Fact]
    public async Task HandleRejectsInvalidSlug()
    {
        var reader =
            new StubStorefrontProductReader();

        var handler =
            new GetPublishedProductBySlugQueryHandler(
                reader);

        var result = await handler.HandleAsync(
            new GetPublishedProductBySlugQuery(
                "Invalid Slug"),
            TestContext.Current
                .CancellationToken);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.InvalidSlug",
            result.Error?.Code);

        Assert.Equal(0, reader.CallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFound()
    {
        var reader =
            new StubStorefrontProductReader();

        var handler =
            new GetPublishedProductBySlugQueryHandler(
                reader);

        var result = await handler.HandleAsync(
            new GetPublishedProductBySlugQuery(
                "missing-product"),
            TestContext.Current
                .CancellationToken);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Storefront.ProductNotFound",
            result.Error?.Code);
    }

    private static PublishedProductDetailsReadModel
        CreateReadModel(string slug)
    {
        return new PublishedProductDetailsReadModel(
            Guid.CreateVersion7(),
            "Enterprise Keyboard",
            slug,
            "Description",
            1,
            [],
            []);
    }

    private sealed class
        StubStorefrontProductReader :
        IStorefrontProductReader
    {
        public PublishedProductDetailsReadModel?
            ProductToReturn
        { get; set; }

        public int CallCount { get; private set; }

        public Task<
            PublishedProductDetailsReadModel?>
            GetBySlugAsync(
                ProductSlug slug,
                CancellationToken cancellationToken)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            CallCount++;

            return Task.FromResult(
                ProductToReturn);
        }
    }
}
