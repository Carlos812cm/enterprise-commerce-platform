using Catalog.Api.Authorization;
using Catalog.Api.Errors;
using Catalog.Application.Products.GetProductById;
using Commerce.Application.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Catalog.Api.Endpoints.Products.GetProductById;

internal static class GetProductByIdEndpoint
{
    public static RouteHandlerBuilder MapGetProductById(
        this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return group
            .MapGet(
                "/products/{productId:guid}",
                HandleAsync)
            .WithName("GetProductById")
            .WithSummary(
                "Get administrative Catalog product details")
            .WithDescription(
                "Returns the complete administrative product read model, including Draft and Discontinued state.")
            .WithTags("Catalog Products")
            .Produces<GetProductByIdHttpResponse>(
                StatusCodes.Status200OK)
            .ProducesProblem(
                StatusCodes.Status400BadRequest)
            .ProducesProblem(
                StatusCodes.Status401Unauthorized)
            .ProducesProblem(
                StatusCodes.Status403Forbidden)
            .ProducesProblem(
                StatusCodes.Status404NotFound)
            .ProducesProblem(
                StatusCodes.Status500InternalServerError)
            .RequireAuthorization(
                CatalogAuthorization
                    .ManageProductsPolicy);
    }

    private static async Task<
        Results<
            Ok<GetProductByIdHttpResponse>,
            ProblemHttpResult>>
        HandleAsync(
            Guid productId,
            IQueryDispatcher queryDispatcher,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queryDispatcher);
        ArgumentNullException.ThrowIfNull(httpContext);

        var result =
            await queryDispatcher.DispatchAsync(
                    new GetProductByIdQuery(
                        productId),
                    cancellationToken)
                .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return CatalogApiProblemDetails.Create(
                result.Error!,
                httpContext);
        }

        httpContext.Response.Headers[
            "Cache-Control"] =
            "private, no-store";

        return TypedResults.Ok(
            GetProductByIdHttpResponse.From(
                result.Value));
    }
}
