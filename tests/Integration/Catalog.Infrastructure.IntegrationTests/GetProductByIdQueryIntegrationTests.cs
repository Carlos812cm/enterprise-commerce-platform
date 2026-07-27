using Catalog.Application.Abstractions.Persistence;
using Catalog.Application.Products.GetProductById;
using Catalog.Domain.Products;
using Commerce.Application.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class GetProductByIdQueryIntegrationTests :
    IClassFixture<CatalogPostgreSqlFixture>
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(
            2026,
            7,
            26,
            12,
            0,
            0,
            TimeSpan.Zero);

    private readonly CatalogPostgreSqlFixture _fixture;

    public GetProductByIdQueryIntegrationTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task QueryReturnsCompletePublishedProductReadModel()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var product = CreatePublishedProduct();

        await using (var writeScope =
            serviceProvider.CreateAsyncScope())
        {
            var repository =
                writeScope.ServiceProvider
                    .GetRequiredService<
                        IProductRepository>();

            var unitOfWork =
                writeScope.ServiceProvider
                    .GetRequiredService<
                        ICatalogUnitOfWork>();

            repository.Add(product);

            await unitOfWork.SaveChangesAsync(
                TestContext.Current
                    .CancellationToken);
        }

        await using var queryScope =
            serviceProvider.CreateAsyncScope();

        var dispatcher =
            queryScope.ServiceProvider
                .GetRequiredService<
                    IQueryDispatcher>();

        var result = await dispatcher.DispatchAsync(
            new GetProductByIdQuery(
                product.Id.Value),
            TestContext.Current
                .CancellationToken);

        Assert.True(
            result.IsSuccess,
            result.Error?.Code);

        var readModel = result.Value;

        Assert.Equal(
            product.Id.Value,
            readModel.ProductId);

        Assert.Equal(
            "published",
            readModel.Status);

        Assert.Equal(
            1,
            readModel.Version);

        Assert.Equal(
            2,
            readModel.Options.Count);

        Assert.Equal(
            2,
            readModel.Variants.Count);

        Assert.All(
            readModel.Variants,
            variant =>
            {
                Assert.Equal(
                    "active",
                    variant.Status);

                Assert.Equal(
                    2,
                    variant.Selections.Count);
            });
    }

    [Fact]
    public async Task QueryReturnsNotFoundForMissingProduct()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        await using var scope =
            serviceProvider.CreateAsyncScope();

        var dispatcher =
            scope.ServiceProvider
                .GetRequiredService<
                    IQueryDispatcher>();

        var result = await dispatcher.DispatchAsync(
            new GetProductByIdQuery(
                Guid.CreateVersion7()),
            TestContext.Current
                .CancellationToken);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.NotFound",
            result.Error?.Code);
    }

    private static Product CreatePublishedProduct()
    {
        var suffix =
            Guid.CreateVersion7()
                .ToString("N");

        var product = Product.CreateDraft(
            ProductName.Create(
                "Query Product").Value,
            ProductSlug.Create(
                $"query-product-{suffix}").Value,
            ProductDescription.Create(
                "Product queried through Dapper.").Value,
            CreatedAtUtc);

        var colorId = product.DefineOption(
            OptionName.Create("Color").Value,
            displayOrder: 0).Value;

        var sizeId = product.DefineOption(
            OptionName.Create("Size").Value,
            displayOrder: 1).Value;

        product.AddVariant(
            Sku.Create(
                $"QUERY-{suffix}-BLK").Value,
            CreateCombination(
                (colorId, "Black"),
                (sizeId, "42")),
            CreatedAtUtc.AddMinutes(1));

        product.AddVariant(
            Sku.Create(
                $"QUERY-{suffix}-WHT").Value,
            CreateCombination(
                (colorId, "White"),
                (sizeId, "42")),
            CreatedAtUtc.AddMinutes(2));

        var publishResult = product.Publish(
            CreatedAtUtc.AddMinutes(3));

        Assert.True(
            publishResult.IsSuccess,
            publishResult.Error?.Code);

        return product;
    }

    private static VariantOptionCombination
        CreateCombination(
            params (
                ProductOptionId OptionId,
                string Value)[] values)
    {
        var selections = values
            .Select(
                value =>
                    OptionSelection.Create(
                        value.OptionId,
                        OptionValue.Create(
                            value.Value).Value))
            .ToArray();

        return VariantOptionCombination.Create(
            selections).Value;
    }
}
