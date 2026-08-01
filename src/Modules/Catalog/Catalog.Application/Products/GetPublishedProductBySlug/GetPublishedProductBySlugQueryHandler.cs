using Catalog.Application.Abstractions.Queries;
using Catalog.Domain.Products;
using Commerce.Application.Messaging;
using Commerce.Domain;

namespace Catalog.Application.Products.GetPublishedProductBySlug;

public sealed class GetPublishedProductBySlugQueryHandler :
    IQueryHandler<
        GetPublishedProductBySlugQuery,
        PublishedProductDetailsReadModel>
{
    private readonly IStorefrontProductReader
        _productReader;

    public GetPublishedProductBySlugQueryHandler(
        IStorefrontProductReader productReader)
    {
        ArgumentNullException.ThrowIfNull(
            productReader);

        _productReader = productReader;
    }

    public async Task<
        Result<PublishedProductDetailsReadModel>>
        HandleAsync(
            GetPublishedProductBySlugQuery query,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        cancellationToken.ThrowIfCancellationRequested();

        var slugResult =
            ProductSlug.Create(query.Slug);

        if (slugResult.IsFailure)
        {
            return Result.Failure<
                PublishedProductDetailsReadModel>(
                slugResult.Error!);
        }

        var product =
            await _productReader
                .GetBySlugAsync(
                    slugResult.Value,
                    cancellationToken)
                .ConfigureAwait(false);

        if (product is null)
        {
            return Result.Failure<
                PublishedProductDetailsReadModel>(
                GetPublishedProductBySlugErrors
                    .NotFound);
        }

        return Result.Success(product);
    }
}
