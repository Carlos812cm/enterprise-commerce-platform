using Catalog.Application.Abstractions.Persistence;
using Catalog.Domain.Products;
using Commerce.Application.Messaging;
using Commerce.Application.Persistence;
using Commerce.Domain;

namespace Catalog.Application.Products.CreateDraftProduct;

public sealed class CreateDraftProductCommandHandler :
    ICommandHandler<
        CreateDraftProductCommand,
        CreateDraftProductResponse>
{
    private readonly IProductRepository _productRepository;
    private readonly IProductSlugUniquenessChecker _slugUniquenessChecker;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public CreateDraftProductCommandHandler(
        IProductRepository productRepository,
        IProductSlugUniquenessChecker slugUniquenessChecker,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(productRepository);
        ArgumentNullException.ThrowIfNull(slugUniquenessChecker);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _productRepository = productRepository;
        _slugUniquenessChecker = slugUniquenessChecker;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    public async Task<Result<CreateDraftProductResponse>> HandleAsync(
        CreateDraftProductCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        cancellationToken.ThrowIfCancellationRequested();

        var nameResult =
            ProductName.Create(command.Name);

        if (nameResult.IsFailure)
        {
            return Result.Failure<CreateDraftProductResponse>(
                nameResult.Error!);
        }

        var slugResult =
            ProductSlug.Create(command.Slug);

        if (slugResult.IsFailure)
        {
            return Result.Failure<CreateDraftProductResponse>(
                slugResult.Error!);
        }

        var descriptionResult =
            ProductDescription.Create(command.Description);

        if (descriptionResult.IsFailure)
        {
            return Result.Failure<CreateDraftProductResponse>(
                descriptionResult.Error!);
        }

        var slugIsUnique =
            await _slugUniquenessChecker
                .IsUniqueAsync(
                    slugResult.Value,
                    cancellationToken)
                .ConfigureAwait(false);

        if (!slugIsUnique)
        {
            return Result.Failure<CreateDraftProductResponse>(
                CreateDraftProductErrors.SlugAlreadyExists);
        }

        var product = Product.CreateDraft(
            nameResult.Value,
            slugResult.Value,
            descriptionResult.Value,
            _timeProvider.GetUtcNow());

        _productRepository.Add(product);

        await _unitOfWork
            .SaveChangesAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(
            new CreateDraftProductResponse(
                product.Id,
                product.Status));
    }
}
