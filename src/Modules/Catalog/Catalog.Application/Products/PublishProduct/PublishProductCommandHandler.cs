using Catalog.Application.Abstractions.Persistence;
using Catalog.Domain.Products;
using Commerce.Application.Messaging;
using Commerce.Domain;

namespace Catalog.Application.Products.PublishProduct;

public sealed class PublishProductCommandHandler :
    ICommandHandler<PublishProductCommand, PublishProductResponse>
{
    private readonly IProductRepository _productRepository;
    private readonly ICatalogUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public PublishProductCommandHandler(
        IProductRepository productRepository,
        ICatalogUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(productRepository);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _productRepository = productRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<Result<PublishProductResponse>> HandleAsync(
        PublishProductCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (command.ProductId == Guid.Empty)
        {
            return Result.Failure<PublishProductResponse>(
                PublishProductErrors.InvalidProductId);
        }

        var product = await _productRepository
            .GetByIdAsync(
                ProductId.Create(command.ProductId),
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (product is null)
        {
            return Result.Failure<PublishProductResponse>(
                PublishProductErrors.NotFound);
        }

        var publishedAtUtc = _timeProvider.GetUtcNow();
        var publication = product.Publish(publishedAtUtc);

        if (publication.IsFailure)
        {
            return Result.Failure<PublishProductResponse>(
                publication.Error!);
        }

        _productRepository.Update(product);

        try
        {
            await _unitOfWork
                .SaveChangesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (CatalogOptimisticConcurrencyException)
        {
            return Result.Failure<PublishProductResponse>(
                PublishProductErrors.ConcurrencyConflict);
        }

        return Result.Success(
            new PublishProductResponse(
                product.Id,
                product.Status,
                publishedAtUtc));
    }
}
