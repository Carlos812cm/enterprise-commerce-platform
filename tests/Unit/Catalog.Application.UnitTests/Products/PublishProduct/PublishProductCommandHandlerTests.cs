using Catalog.Application.Abstractions.Persistence;
using Catalog.Application.Products.PublishProduct;
using Catalog.Domain.Products;
using Catalog.Domain.Products.Events;
using Commerce.Domain;
using Xunit;

namespace Catalog.Application.UnitTests.Products.PublishProduct;

public sealed class PublishProductCommandHandlerTests
{
    private static readonly DateTimeOffset PublicationTime =
        new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var fixture = new HandlerFixture(null);

        Assert.Throws<ArgumentNullException>(
            "productRepository",
            () => new PublishProductCommandHandler(
                null!, fixture.UnitOfWork, fixture.Clock));

        Assert.Throws<ArgumentNullException>(
            "unitOfWork",
            () => new PublishProductCommandHandler(
                fixture.Repository, null!, fixture.Clock));

        Assert.Throws<ArgumentNullException>(
            "timeProvider",
            () => new PublishProductCommandHandler(
                fixture.Repository, fixture.UnitOfWork, null!));
    }

    [Fact]
    public async Task HandleRejectsNullCommandBeforeSideEffects()
    {
        var fixture = new HandlerFixture(null);

        await Assert.ThrowsAsync<ArgumentNullException>(
            "command",
            () => fixture.Handler.HandleAsync(null!, CancellationToken.None));

        Assert.Empty(fixture.Calls);
        Assert.Equal(0, fixture.Clock.ReadCount);
    }

    [Fact]
    public async Task HandlePublishesAllDraftVariantsAndRequestsOneSave()
    {
        var product = CreateProduct();
        var fixture = new HandlerFixture(product);
        using var cancellation = new CancellationTokenSource();

        var result = await fixture.Handler.HandleAsync(
            new PublishProductCommand(product.Id.Value),
            cancellation.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(product.Id, result.Value.ProductId);
        Assert.Equal(ProductStatus.Published, result.Value.Status);
        Assert.Equal(PublicationTime, result.Value.PublishedAtUtc);
        Assert.Equal(PublicationTime, product.PublishedAtUtc);
        Assert.Equal(2, product.Variants.Count);
        Assert.All(product.Variants, variant =>
        {
            Assert.Equal(ProductVariantStatus.Active, variant.Status);
            Assert.Equal(PublicationTime, variant.ActivatedAtUtc);
        });

        Assert.Collection(
            fixture.Calls,
            call => Assert.Equal("load", call),
            call => Assert.Equal("update", call),
            call => Assert.Equal("save", call));

        Assert.Equal(product.Id, fixture.Repository.ObservedProductId);
        Assert.Equal(cancellation.Token, fixture.Repository.ObservedToken);
        Assert.Equal(cancellation.Token, fixture.UnitOfWork.ObservedToken);
        Assert.Same(product, fixture.Repository.UpdatedProduct);
        Assert.Equal(1, fixture.Clock.ReadCount);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);

        // The fake does not commit or clear events. Cleanup belongs to the real unit of work.
        Assert.Collection(
            product.DomainEvents,
            domainEvent => Assert.IsType<ProductVariantActivatedDomainEvent>(domainEvent),
            domainEvent => Assert.IsType<ProductVariantActivatedDomainEvent>(domainEvent),
            domainEvent =>
            {
                var published = Assert.IsType<ProductPublishedDomainEvent>(domainEvent);
                Assert.Equal(product.Id, published.ProductId);
                Assert.Equal(product.Slug, published.Slug);
                Assert.Equal(PublicationTime, published.OccurredAtUtc);
            });
    }

    [Fact]
    public async Task HandleRejectsEmptyIdBeforeRepositoryAccess()
    {
        var fixture = new HandlerFixture(null);

        var result = await fixture.Handler.HandleAsync(
            new PublishProductCommand(Guid.Empty),
            CancellationToken.None);

        AssertFailure(result, "Catalog.Product.InvalidId", ErrorType.Validation);
        Assert.Empty(fixture.Calls);
        Assert.Equal(0, fixture.Clock.ReadCount);
    }

    [Fact]
    public async Task HandleReturnsNotFoundWithoutWriting()
    {
        var fixture = new HandlerFixture(null);

        var result = await fixture.Handler.HandleAsync(
            new PublishProductCommand(Guid.CreateVersion7()),
            CancellationToken.None);

        AssertFailure(result, "Catalog.Product.NotFound", ErrorType.NotFound);
        AssertOnlyLoaded(fixture);
        Assert.Equal(0, fixture.Clock.ReadCount);
    }

    [Fact]
    public async Task HandleRejectsDraftWithoutPublishableVariants()
    {
        var product = CreateProduct(withVariants: false);
        var fixture = new HandlerFixture(product);

        var result = await fixture.Handler.HandleAsync(
            new PublishProductCommand(product.Id.Value),
            CancellationToken.None);

        AssertFailure(result, "Catalog.Product.NoPublishableVariants", ErrorType.Validation);
        AssertOnlyLoaded(fixture);
        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Null(product.PublishedAtUtc);
        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public async Task HandleRejectsAlreadyPublishedWithoutChangingTimestamp()
    {
        var product = CreateProduct();
        var originalTime = PublicationTime.AddMinutes(-5);
        Assert.True(product.Publish(originalTime).IsSuccess);
        _ = product.DequeueDomainEvents();
        var fixture = new HandlerFixture(product);

        var result = await fixture.Handler.HandleAsync(
            new PublishProductCommand(product.Id.Value),
            CancellationToken.None);

        AssertFailure(result, "Catalog.Product.AlreadyPublished", ErrorType.Conflict);
        AssertOnlyLoaded(fixture);
        Assert.Equal(originalTime, product.PublishedAtUtc);
        Assert.All(product.Variants, variant =>
            Assert.Equal(originalTime, variant.ActivatedAtUtc));
        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public async Task HandleRejectsDiscontinuedWithoutWriting()
    {
        var product = CreateProduct();
        var originalTime = PublicationTime.AddMinutes(-5);
        Assert.True(product.Discontinue(originalTime).IsSuccess);
        _ = product.DequeueDomainEvents();
        var fixture = new HandlerFixture(product);

        var result = await fixture.Handler.HandleAsync(
            new PublishProductCommand(product.Id.Value),
            CancellationToken.None);

        AssertFailure(result, "Catalog.Product.IsDiscontinued", ErrorType.Conflict);
        AssertOnlyLoaded(fixture);
        Assert.Equal(ProductStatus.Discontinued, product.Status);
        Assert.Equal(originalTime, product.DiscontinuedAtUtc);
        Assert.Null(product.PublishedAtUtc);
        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public async Task HandleRejectsPreCancelledRequestBeforeRepositoryAccess()
    {
        var product = CreateProduct();
        var fixture = new HandlerFixture(product);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.Handler.HandleAsync(
                new PublishProductCommand(product.Id.Value),
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Empty(fixture.Calls);
        Assert.Equal(0, fixture.Clock.ReadCount);
        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public async Task HandleObservesCancellationAfterLoadingBeforeMutation()
    {
        var product = CreateProduct();
        var fixture = new HandlerFixture(product);
        using var cancellation = new CancellationTokenSource();
        fixture.Repository.AfterRead = cancellation.Cancel;

        var exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.Handler.HandleAsync(
                new PublishProductCommand(product.Id.Value),
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        AssertOnlyLoaded(fixture);
        Assert.Equal(0, fixture.Clock.ReadCount);
        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Null(product.PublishedAtUtc);
        Assert.Empty(product.DomainEvents);
    }

    [Fact]
    public async Task HandleTranslatesConcurrencyFailureWithoutRetry()
    {
        var product = CreateProduct();
        var fixture = new HandlerFixture(product);
        fixture.UnitOfWork.Failure = new CatalogOptimisticConcurrencyException(
            "Provider-specific diagnostic that must not become the result.",
            new InvalidOperationException("Internal persistence detail."));

        var result = await fixture.Handler.HandleAsync(
            new PublishProductCommand(product.Id.Value),
            CancellationToken.None);

        AssertFailure(result, "Catalog.Product.ConcurrencyConflict", ErrorType.Conflict);
        Assert.Equal(
            "The product changed while publication was being processed. Reload it and retry.",
            result.Error!.Description);
        Assert.Equal(1, fixture.Repository.ReadCount);
        Assert.Equal(1, fixture.Repository.UpdateCount);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Equal(3, product.DomainEvents.Count);
    }

    [Fact]
    public async Task HandlePropagatesUnexpectedPersistenceFailure()
    {
        var product = CreateProduct();
        var fixture = new HandlerFixture(product);
        var expected = new InvalidOperationException("Storage failure.");
        fixture.UnitOfWork.Failure = expected;

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Handler.HandleAsync(
                new PublishProductCommand(product.Id.Value),
                CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Equal(3, product.DomainEvents.Count);
    }

    [Fact]
    public async Task HandlePropagatesPersistenceCancellationWithoutRetry()
    {
        var product = CreateProduct();
        var fixture = new HandlerFixture(product);
        using var cancellation = new CancellationTokenSource();
        var expected = new OperationCanceledException(cancellation.Token);
        fixture.UnitOfWork.Failure = expected;

        var actual = await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.Handler.HandleAsync(
                new PublishProductCommand(product.Id.Value),
                cancellation.Token));

        Assert.Same(expected, actual);
        Assert.Equal(cancellation.Token, fixture.UnitOfWork.ObservedToken);
        Assert.Equal(1, fixture.UnitOfWork.SaveCount);
        Assert.Equal(3, product.DomainEvents.Count);
    }

    [Fact]
    public async Task HandlePropagatesRepositoryFailureWithoutWriting()
    {
        var product = CreateProduct();
        var fixture = new HandlerFixture(product);
        var expected = new InvalidOperationException("Read failure.");
        fixture.Repository.Failure = expected;

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => fixture.Handler.HandleAsync(
                new PublishProductCommand(product.Id.Value),
                CancellationToken.None));

        Assert.Same(expected, actual);
        AssertOnlyLoaded(fixture);
        Assert.Equal(0, fixture.Clock.ReadCount);
        Assert.Equal(ProductStatus.Draft, product.Status);
        Assert.Empty(product.DomainEvents);
    }

    private static void AssertFailure(
        Result<PublishProductResponse> result,
        string code,
        ErrorType type)
    {
        Assert.True(result.IsFailure);
        Assert.NotNull(result.Error);
        Assert.Equal(code, result.Error.Code);
        Assert.Equal(type, result.Error.Type);
    }

    private static void AssertOnlyLoaded(HandlerFixture fixture)
    {
        Assert.Equal("load", Assert.Single(fixture.Calls));
        Assert.Equal(1, fixture.Repository.ReadCount);
        Assert.Equal(0, fixture.Repository.UpdateCount);
        Assert.Equal(0, fixture.UnitOfWork.SaveCount);
    }

    private static Product CreateProduct(bool withVariants = true)
    {
        var product = Product.CreateDraft(
            ProductName.Create("Publication test product").Value,
            ProductSlug.Create("publication-test-product").Value,
            ProductDescription.Empty,
            PublicationTime.AddHours(-1));

        if (withVariants)
        {
            var option = product.DefineOption(OptionName.Create("Color").Value, 0);
            Assert.True(option.IsSuccess);

            foreach (var color in new[] { "Black", "White" })
            {
                var combination = VariantOptionCombination.Create(
                    [OptionSelection.Create(option.Value, OptionValue.Create(color).Value)]);
                Assert.True(combination.IsSuccess);

                var added = product.AddVariant(
                    Sku.Create($"PUB-{color.ToUpperInvariant()}").Value,
                    combination.Value,
                    PublicationTime.AddMinutes(-30));
                Assert.True(added.IsSuccess);
            }
        }

        _ = product.DequeueDomainEvents();
        return product;
    }

    private sealed class HandlerFixture
    {
        public HandlerFixture(Product? product)
        {
            Repository = new RecordingProductRepository(product, Calls);
            UnitOfWork = new RecordingUnitOfWork(Calls);
            Handler = new PublishProductCommandHandler(Repository, UnitOfWork, Clock);
        }

        public List<string> Calls { get; } = [];
        public RecordingClock Clock { get; } = new();
        public RecordingProductRepository Repository { get; }
        public RecordingUnitOfWork UnitOfWork { get; }
        public PublishProductCommandHandler Handler { get; }
    }

    private sealed class RecordingClock : TimeProvider
    {
        public int ReadCount { get; private set; }

        public override DateTimeOffset GetUtcNow()
        {
            ReadCount++;
            return PublicationTime;
        }
    }

    private sealed class RecordingProductRepository(Product? initialProduct, List<string> calls) :
        IProductRepository
    {
        public int ReadCount { get; private set; }
        public int UpdateCount { get; private set; }
        public ProductId ObservedProductId { get; private set; }
        public CancellationToken ObservedToken { get; private set; }
        public Product? UpdatedProduct { get; private set; }
        public Exception? Failure { get; set; }
        public Action? AfterRead { get; set; }

        public Task<Product?> GetByIdAsync(
            ProductId productId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("load");
            ReadCount++;
            ObservedProductId = productId;
            ObservedToken = cancellationToken;

            if (Failure is { } failure)
            {
                return Task.FromException<Product?>(failure);
            }

            AfterRead?.Invoke();
            return Task.FromResult(initialProduct);
        }

        public void Add(Product product)
        {
            ArgumentNullException.ThrowIfNull(product);
            throw new InvalidOperationException("Publication must not add a new aggregate.");
        }

        public void Update(Product product)
        {
            ArgumentNullException.ThrowIfNull(product);
            calls.Add("update");
            UpdateCount++;
            UpdatedProduct = product;
        }
    }

    private sealed class RecordingUnitOfWork(List<string> calls) : ICatalogUnitOfWork
    {
        public int SaveCount { get; private set; }
        public CancellationToken ObservedToken { get; private set; }
        public Exception? Failure { get; set; }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("save");
            SaveCount++;
            ObservedToken = cancellationToken;

            return Failure is { } failure
                ? Task.FromException(failure)
                : Task.CompletedTask;
        }
    }
}
