using Catalog.Application.Abstractions.Queries;
using Commerce.Application.Messaging;
using Commerce.Domain;

namespace Catalog.Application.Products.GetProductById;

public sealed class GetProductByIdQueryHandler :
    IQueryHandler<
        GetProductByIdQuery,
        AdminProductDetailsReadModel>
{
    private readonly IProductDetailsReader _productDetailsReader;

    public GetProductByIdQueryHandler(
        IProductDetailsReader productDetailsReader)
    {
        ArgumentNullException.ThrowIfNull(
            productDetailsReader);

        _productDetailsReader =
            productDetailsReader;
    }

    public async Task<
        Result<AdminProductDetailsReadModel>>
        HandleAsync(
            GetProductByIdQuery query,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        cancellationToken.ThrowIfCancellationRequested();

        if (query.ProductId == Guid.Empty)
        {
            return Result.Failure<
                AdminProductDetailsReadModel>(
                GetProductByIdErrors
                    .InvalidProductId);
        }

        var product =
            await _productDetailsReader
                .GetByIdAsync(
                    query.ProductId,
                    cancellationToken)
                .ConfigureAwait(false);

        if (product is null)
        {
            return Result.Failure<
                AdminProductDetailsReadModel>(
                GetProductByIdErrors.NotFound);
        }

        return Result.Success(product);
    }
}
