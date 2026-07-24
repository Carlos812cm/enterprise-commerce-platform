using Catalog.Application.Abstractions.Persistence;
using Catalog.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Catalog.Infrastructure.IntegrationTests;

public sealed class ProductAggregatePersistenceTests :
    IClassFixture<CatalogPostgreSqlFixture>
{
    private static readonly DateTimeOffset CreatedAtUtc =
        new(
            2026,
            7,
            23,
            12,
            0,
            0,
            TimeSpan.Zero);

    private readonly CatalogPostgreSqlFixture _fixture;

    public ProductAggregatePersistenceTests(
        CatalogPostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task CompleteAggregateRoundTripsWithoutReplayingEvents()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var product =
            CreatePublishedConfigurableProduct(
                $"aggregate-{Guid.CreateVersion7():N}",
                $"AGG-{Guid.CreateVersion7():N}");

        product.DequeueDomainEvents();

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

        await using var readScope =
            serviceProvider.CreateAsyncScope();

        var readRepository =
            readScope.ServiceProvider
                .GetRequiredService<
                    IProductRepository>();

        var rehydrated =
            await readRepository.GetByIdAsync(
                product.Id,
                TestContext.Current
                    .CancellationToken);

        Assert.NotNull(rehydrated);

        Assert.Equal(
            ProductStatus.Published,
            rehydrated.Status);

        Assert.Equal(
            2,
            rehydrated.OptionDefinitions.Count);

        Assert.Equal(
            2,
            rehydrated.Variants.Count);

        Assert.All(
            rehydrated.Variants,
            variant =>
                Assert.Equal(
                    ProductVariantStatus.Active,
                    variant.Status));

        Assert.All(
            rehydrated.Variants,
            variant =>
                Assert.Equal(
                    2,
                    variant.OptionCombination.Count));

        Assert.Empty(
            rehydrated.DomainEvents);
    }

    [Fact]
    public async Task DraftVariantOptionChangesArePersisted()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var product = Product.CreateDraft(
            ProductName.Create(
                "Option Update Product").Value,
            ProductSlug.Create(
                $"option-update-{Guid.CreateVersion7():N}").Value,
            ProductDescription.Empty,
            CreatedAtUtc);

        var colorId = product.DefineOption(
            OptionName.Create("Color").Value,
            displayOrder: 0).Value;

        var variantId = product.AddVariant(
            Sku.Create(
                $"OPT-{Guid.CreateVersion7():N}").Value,
            CreateCombination(
                (colorId, "Black")),
            CreatedAtUtc.AddMinutes(1)).Value;

        await using (var seedScope =
            serviceProvider.CreateAsyncScope())
        {
            var repository =
                seedScope.ServiceProvider
                    .GetRequiredService<
                        IProductRepository>();

            var unitOfWork =
                seedScope.ServiceProvider
                    .GetRequiredService<
                        ICatalogUnitOfWork>();

            repository.Add(product);

            await unitOfWork.SaveChangesAsync(
                TestContext.Current
                    .CancellationToken);
        }

        await using (var updateScope =
            serviceProvider.CreateAsyncScope())
        {
            var repository =
                updateScope.ServiceProvider
                    .GetRequiredService<
                        IProductRepository>();

            var unitOfWork =
                updateScope.ServiceProvider
                    .GetRequiredService<
                        ICatalogUnitOfWork>();

            var loaded =
                await repository.GetByIdAsync(
                    product.Id,
                    TestContext.Current
                        .CancellationToken);

            Assert.NotNull(loaded);

            var result =
                loaded.ChangeVariantOptionCombination(
                    variantId,
                    CreateCombination(
                        (colorId, "White")));

            Assert.True(
                result.IsSuccess,
                result.Error?.Code);

            repository.Update(loaded);

            await unitOfWork.SaveChangesAsync(
                TestContext.Current
                    .CancellationToken);
        }

        await using var verificationScope =
            serviceProvider.CreateAsyncScope();

        var verificationRepository =
            verificationScope.ServiceProvider
                .GetRequiredService<
                    IProductRepository>();

        var verified =
            await verificationRepository.GetByIdAsync(
                product.Id,
                TestContext.Current
                    .CancellationToken);

        Assert.NotNull(verified);

        var variant =
            Assert.Single(verified.Variants);

        Assert.Equal(
            "White",
            Assert.Single(
                variant.OptionCombination.Selections)
                .Value
                .Value);
    }

    [Fact]
    public async Task ConcurrentAggregateUpdatesAreRejected()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var product = Product.CreateDraft(
            ProductName.Create(
                "Concurrent Product").Value,
            ProductSlug.Create(
                $"concurrency-{Guid.CreateVersion7():N}").Value,
            ProductDescription.Empty,
            CreatedAtUtc);

        await using (var seedScope =
            serviceProvider.CreateAsyncScope())
        {
            var repository =
                seedScope.ServiceProvider
                    .GetRequiredService<
                        IProductRepository>();

            var unitOfWork =
                seedScope.ServiceProvider
                    .GetRequiredService<
                        ICatalogUnitOfWork>();

            repository.Add(product);

            await unitOfWork.SaveChangesAsync(
                TestContext.Current
                    .CancellationToken);
        }

        await using var firstScope =
            serviceProvider.CreateAsyncScope();

        await using var secondScope =
            serviceProvider.CreateAsyncScope();

        var firstRepository =
            firstScope.ServiceProvider
                .GetRequiredService<
                    IProductRepository>();

        var secondRepository =
            secondScope.ServiceProvider
                .GetRequiredService<
                    IProductRepository>();

        var firstUnitOfWork =
            firstScope.ServiceProvider
                .GetRequiredService<
                    ICatalogUnitOfWork>();

        var secondUnitOfWork =
            secondScope.ServiceProvider
                .GetRequiredService<
                    ICatalogUnitOfWork>();

        var firstProduct =
            await firstRepository.GetByIdAsync(
                product.Id,
                TestContext.Current
                    .CancellationToken);

        var secondProduct =
            await secondRepository.GetByIdAsync(
                product.Id,
                TestContext.Current
                    .CancellationToken);

        Assert.NotNull(firstProduct);
        Assert.NotNull(secondProduct);

        Assert.True(
            firstProduct.ChangeName(
                ProductName.Create(
                    "First Writer").Value)
                .IsSuccess);

        Assert.True(
            secondProduct.ChangeName(
                ProductName.Create(
                    "Second Writer").Value)
                .IsSuccess);

        firstRepository.Update(
            firstProduct);

        secondRepository.Update(
            secondProduct);

        await firstUnitOfWork.SaveChangesAsync(
            TestContext.Current
                .CancellationToken);

        await Assert.ThrowsAsync<
            DbUpdateConcurrencyException>(
            () => secondUnitOfWork
                .SaveChangesAsync(
                    TestContext.Current
                        .CancellationToken));
    }

    [Fact]
    public async Task SkuIsGloballyUniqueAcrossProducts()
    {
        await using var serviceProvider =
            _fixture.CreateServiceProvider(
                TimeProvider.System);

        var sku =
            $"GLOBAL-{Guid.CreateVersion7():N}";

        var first = CreateSimpleDraftProduct(
            $"first-{Guid.CreateVersion7():N}",
            sku);

        var second = CreateSimpleDraftProduct(
            $"second-{Guid.CreateVersion7():N}",
            sku);

        await using var scope =
            serviceProvider.CreateAsyncScope();

        var repository =
            scope.ServiceProvider
                .GetRequiredService<
                    IProductRepository>();

        var unitOfWork =
            scope.ServiceProvider
                .GetRequiredService<
                    ICatalogUnitOfWork>();

        repository.Add(first);
        repository.Add(second);

        var exception =
            await Assert.ThrowsAsync<
                DbUpdateException>(
                () => unitOfWork
                    .SaveChangesAsync(
                        TestContext.Current
                            .CancellationToken));

        var postgresException =
            Assert.IsType<PostgresException>(
                exception.InnerException);

        Assert.Equal(
            PostgresErrorCodes.UniqueViolation,
            postgresException.SqlState);

        Assert.Equal(
            "ux_product_variants_sku",
            postgresException.ConstraintName);
    }

    private static Product CreatePublishedConfigurableProduct(
        string slug,
        string skuPrefix)
    {
        var product = Product.CreateDraft(
            ProductName.Create(
                "Configurable Product").Value,
            ProductSlug.Create(slug).Value,
            ProductDescription.Empty,
            CreatedAtUtc);

        var colorId = product.DefineOption(
            OptionName.Create("Color").Value,
            displayOrder: 0).Value;

        var sizeId = product.DefineOption(
            OptionName.Create("Size").Value,
            displayOrder: 1).Value;

        product.AddVariant(
            Sku.Create($"{skuPrefix}-BLACK-42").Value,
            CreateCombination(
                (colorId, "Black"),
                (sizeId, "42")),
            CreatedAtUtc.AddMinutes(1));

        product.AddVariant(
            Sku.Create($"{skuPrefix}-WHITE-42").Value,
            CreateCombination(
                (colorId, "White"),
                (sizeId, "42")),
            CreatedAtUtc.AddMinutes(2));

        var publishResult =
            product.Publish(
                CreatedAtUtc.AddMinutes(3));

        Assert.True(
            publishResult.IsSuccess,
            publishResult.Error?.Code);

        return product;
    }

    private static Product CreateSimpleDraftProduct(
        string slug,
        string sku)
    {
        var product = Product.CreateDraft(
            ProductName.Create(
                "Simple Product").Value,
            ProductSlug.Create(slug).Value,
            ProductDescription.Empty,
            CreatedAtUtc);

        var result = product.AddVariant(
            Sku.Create(sku).Value,
            VariantOptionCombination.Empty,
            CreatedAtUtc.AddMinutes(1));

        Assert.True(
            result.IsSuccess,
            result.Error?.Code);

        return product;
    }

    private static VariantOptionCombination CreateCombination(
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
