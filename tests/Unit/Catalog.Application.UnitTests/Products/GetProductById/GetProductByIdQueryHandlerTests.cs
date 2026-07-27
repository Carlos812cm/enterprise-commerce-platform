using Catalog.Application.Abstractions.Queries;
using Catalog.Application.Products.GetProductById;
using Xunit;

namespace Catalog.Application.UnitTests.Products.GetProductById;

public sealed class GetProductByIdQueryHandlerTests
{
    [Fact]
    public async Task HandleReturnsProductDetails()
    {
        var productId = Guid.CreateVersion7();

        var expected =
            CreateReadModel(productId);

        var reader =
            new StubProductDetailsReader
            {
                ProductToReturn = expected
            };

        var handler =
            new GetProductByIdQueryHandler(
                reader);

        var result = await handler.HandleAsync(
            new GetProductByIdQuery(
                productId),
            TestContext.Current
                .CancellationToken);

        Assert.True(result.IsSuccess);
        Assert.Same(expected, result.Value);

        Assert.Equal(
            productId,
            reader.ObservedProductId);

        Assert.Equal(
            1,
            reader.CallCount);
    }

    [Fact]
    public async Task HandleRejectsEmptyProductId()
    {
        var reader =
            new StubProductDetailsReader();

        var handler =
            new GetProductByIdQueryHandler(
                reader);

        var result = await handler.HandleAsync(
            new GetProductByIdQuery(
                Guid.Empty),
            TestContext.Current
                .CancellationToken);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.InvalidId",
            result.Error?.Code);

        Assert.Equal(
            0,
            reader.CallCount);
    }

    [Fact]
    public async Task HandleReturnsNotFound()
    {
        var reader =
            new StubProductDetailsReader();

        var handler =
            new GetProductByIdQueryHandler(
                reader);

        var result = await handler.HandleAsync(
            new GetProductByIdQuery(
                Guid.CreateVersion7()),
            TestContext.Current
                .CancellationToken);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.NotFound",
            result.Error?.Code);
    }

    [Fact]
    public async Task HandleRejectsPreCancelledOperation()
    {
        var reader =
            new StubProductDetailsReader();

        var handler =
            new GetProductByIdQueryHandler(
                reader);

        using var cancellationTokenSource =
            new CancellationTokenSource();

        await cancellationTokenSource.CancelAsync();

        await Assert.ThrowsAsync<
            OperationCanceledException>(
            () => handler.HandleAsync(
                new GetProductByIdQuery(
                    Guid.CreateVersion7()),
                cancellationTokenSource.Token));

        Assert.Equal(
            0,
            reader.CallCount);
    }

    private static AdminProductDetailsReadModel
        CreateReadModel(Guid productId)
    {
        return new AdminProductDetailsReadModel(
            productId,
            "Enterprise Keyboard",
            "enterprise-keyboard",
            "Description",
            "draft",
            1,
            null,
            null,
            [],
            []);
    }

    private sealed class StubProductDetailsReader :
        IProductDetailsReader
    {
        public AdminProductDetailsReadModel?
            ProductToReturn
        { get; set; }

        public int CallCount { get; private set; }

        public Guid ObservedProductId { get; private set; }

        public Task<AdminProductDetailsReadModel?>
            GetByIdAsync(
                Guid productId,
                CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            ObservedProductId = productId;

            return Task.FromResult(
                ProductToReturn);
        }
    }
}
