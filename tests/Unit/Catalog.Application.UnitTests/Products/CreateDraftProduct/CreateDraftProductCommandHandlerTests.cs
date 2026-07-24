using Catalog.Application.Abstractions.Persistence;
using Catalog.Application.Products.CreateDraftProduct;
using Catalog.Domain.Products;
using Catalog.Domain.Products.Events;
using Commerce.Application.Persistence;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Catalog.Application.UnitTests.Products.CreateDraftProduct;

public sealed class CreateDraftProductCommandHandlerTests
{
    private static readonly DateTimeOffset FixedUtcNow =
        new(
            2026,
            7,
            18,
            12,
            30,
            0,
            TimeSpan.Zero);

    [Fact]
    public async Task HandleCreatesAndPersistsDraftProduct()
    {
        var fixture = new HandlerFixture();

        var command = new CreateDraftProductCommand(
            "  Enterprise   Keyboard ",
            " enterprise-keyboard ",
            "First line\r\nSecond line");

        using var cancellationTokenSource =
            new CancellationTokenSource();

        var result = await fixture.Handler
            .HandleAsync(
                command,
                cancellationTokenSource.Token);

        Assert.True(result.IsSuccess);

        Assert.Equal(
            ProductStatus.Draft,
            result.Value.Status);

        Assert.Equal(
            1,
            fixture.Repository.AddCallCount);

        Assert.Equal(
            1,
            fixture.UnitOfWork.SaveCallCount);

        var product =
            Assert.IsType<Product>(
                fixture.Repository.AddedProduct);

        Assert.Equal(
            result.Value.ProductId,
            product.Id);

        Assert.Equal(
            "Enterprise Keyboard",
            product.Name.Value);

        Assert.Equal(
            "enterprise-keyboard",
            product.Slug.Value);

        Assert.Equal(
            "First line\nSecond line",
            product.Description.Value);

        var domainEvent =
            Assert.IsType<ProductDraftCreatedDomainEvent>(
                Assert.Single(product.DomainEvents));

        Assert.Equal(
            FixedUtcNow,
            domainEvent.OccurredAtUtc);

        Assert.Equal(
            cancellationTokenSource.Token,
            fixture.SlugChecker.ObservedCancellationToken);

        Assert.Equal(
            cancellationTokenSource.Token,
            fixture.UnitOfWork.ObservedCancellationToken);
    }

    [Fact]
    public async Task HandleReturnsNameValidationErrorBeforeSideEffects()
    {
        var fixture = new HandlerFixture();

        var command = new CreateDraftProductCommand(
            " ",
            "valid-product",
            null);

        var result = await fixture.Handler
            .HandleAsync(
                command,
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.NameRequired",
            result.Error?.Code);

        Assert.Equal(
            0,
            fixture.SlugChecker.CallCount);

        Assert.Equal(
            0,
            fixture.Repository.AddCallCount);

        Assert.Equal(
            0,
            fixture.UnitOfWork.SaveCallCount);
    }

    [Fact]
    public async Task HandleReturnsSlugValidationErrorBeforeUniquenessCheck()
    {
        var fixture = new HandlerFixture();

        var command = new CreateDraftProductCommand(
            "Product",
            "Invalid Slug",
            null);

        var result = await fixture.Handler
            .HandleAsync(
                command,
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.InvalidSlug",
            result.Error?.Code);

        Assert.Equal(
            0,
            fixture.SlugChecker.CallCount);

        Assert.Equal(
            0,
            fixture.Repository.AddCallCount);
    }

    [Fact]
    public async Task HandleReturnsDescriptionValidationError()
    {
        var fixture = new HandlerFixture();

        var command = new CreateDraftProductCommand(
            "Product",
            "valid-product",
            new string(
                'x',
                ProductDescription.MaximumLength + 1));

        var result = await fixture.Handler
            .HandleAsync(
                command,
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.DescriptionTooLong",
            result.Error?.Code);

        Assert.Equal(
            0,
            fixture.SlugChecker.CallCount);

        Assert.Equal(
            0,
            fixture.Repository.AddCallCount);
    }

    [Fact]
    public async Task HandleReturnsConflictWhenSlugAlreadyExists()
    {
        var fixture = new HandlerFixture
        {
            SlugIsUnique = false
        };

        var command = new CreateDraftProductCommand(
            "Product",
            "existing-product",
            null);

        var result = await fixture.Handler
            .HandleAsync(
                command,
                CancellationToken.None);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Catalog.Product.SlugAlreadyExists",
            result.Error?.Code);

        Assert.Equal(
            1,
            fixture.SlugChecker.CallCount);

        Assert.Equal(
            0,
            fixture.Repository.AddCallCount);

        Assert.Equal(
            0,
            fixture.UnitOfWork.SaveCallCount);
    }

    [Fact]
    public async Task HandleRejectsPreCancelledOperationWithoutSideEffects()
    {
        var fixture = new HandlerFixture();

        using var cancellationTokenSource =
            new CancellationTokenSource();

        await cancellationTokenSource.CancelAsync();

        var command = new CreateDraftProductCommand(
            "Product",
            "cancelled-product",
            null);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => fixture.Handler.HandleAsync(
                command,
                cancellationTokenSource.Token));

        Assert.Equal(
            0,
            fixture.SlugChecker.CallCount);

        Assert.Equal(
            0,
            fixture.Repository.AddCallCount);

        Assert.Equal(
            0,
            fixture.UnitOfWork.SaveCallCount);
    }

    [Fact]
    public async Task HandlePropagatesTechnicalPersistenceFailure()
    {
        var expectedException =
            new InvalidOperationException(
                "Simulated persistence failure.");

        var fixture = new HandlerFixture
        {
            PersistenceException = expectedException
        };

        var command = new CreateDraftProductCommand(
            "Product",
            "persistence-failure",
            null);

        var actualException =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => fixture.Handler.HandleAsync(
                    command,
                    CancellationToken.None));

        Assert.Same(
            expectedException,
            actualException);

        Assert.Equal(
            1,
            fixture.Repository.AddCallCount);

        Assert.Equal(
            1,
            fixture.UnitOfWork.SaveCallCount);
    }

    private sealed class HandlerFixture
    {
        private readonly RecordingProductRepository _repository =
            new();

        private readonly StubProductSlugUniquenessChecker _slugChecker =
            new();

        private readonly RecordingUnitOfWork _unitOfWork =
            new();

        private readonly FakeTimeProvider _timeProvider =
            new(FixedUtcNow);

        public CreateDraftProductCommandHandler Handler =>
            new(
                _repository,
                _slugChecker,
                _unitOfWork,
                _timeProvider);

        public RecordingProductRepository Repository =>
            _repository;

        public StubProductSlugUniquenessChecker SlugChecker =>
            _slugChecker;

        public RecordingUnitOfWork UnitOfWork =>
            _unitOfWork;

        public bool SlugIsUnique
        {
            set => _slugChecker.IsUnique = value;
        }

        public Exception? PersistenceException
        {
            set => _unitOfWork.ExceptionToThrow = value;
        }
    }

    private sealed class RecordingProductRepository :
        IProductRepository
    {
        public Product? AddedProduct { get; private set; }

        public int AddCallCount { get; private set; }

        public void Add(Product product)
        {
            ArgumentNullException.ThrowIfNull(product);

            AddedProduct = product;
            AddCallCount++;
        }

        public Task<Product?> GetByIdAsync(
        ProductId productId,
        CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult<Product?>(null);
        }

        public void Update(Product product)
        {
            ArgumentNullException.ThrowIfNull(product);
        }
    }

    private sealed class StubProductSlugUniquenessChecker :
        IProductSlugUniquenessChecker
    {
        public bool IsUnique { get; set; } = true;

        public int CallCount { get; private set; }

        public ProductSlug? ObservedSlug { get; private set; }

        public CancellationToken ObservedCancellationToken
        {
            get;
            private set;
        }

        public Task<bool> IsUniqueAsync(
            ProductSlug slug,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(slug);

            cancellationToken.ThrowIfCancellationRequested();

            CallCount++;
            ObservedSlug = slug;
            ObservedCancellationToken = cancellationToken;

            return Task.FromResult(IsUnique);
        }
    }

    private sealed class RecordingUnitOfWork :
        ICatalogUnitOfWork
    {
        public int SaveCallCount { get; private set; }

        public CancellationToken ObservedCancellationToken
        {
            get;
            private set;
        }

        public Exception? ExceptionToThrow { get; set; }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SaveCallCount++;
            ObservedCancellationToken = cancellationToken;

            return ExceptionToThrow is null
                ? Task.CompletedTask
                : Task.FromException(ExceptionToThrow);
        }
    }
}
