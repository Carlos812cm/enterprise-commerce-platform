using Catalog.Api.Authorization;
using Catalog.Api.Errors;
using Catalog.Application.Products.PublishProduct;
using Commerce.Application.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Catalog.Api.Endpoints.Products.PublishProduct;

internal static class PublishProductEndpoint
{
    public static RouteHandlerBuilder MapPublishProduct(
        this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return group
            .MapPost(
                "/products/{productId:guid}/publish",
                HandleAsync)
            .WithName("PublishProduct")
            .WithSummary("Publish a prepared Catalog product")
            .WithDescription(
                "Publishes a draft product and commits its Outbox intents. External delivery is asynchronous. No request body is required.")
            .WithTags("Catalog Products")
            .Produces<PublishProductHttpResponse>(
                StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .RequireAuthorization(
                CatalogAuthorization.ManageProductsPolicy);
    }

    private static async Task<
        Results<Ok<PublishProductHttpResponse>, ProblemHttpResult>>
        HandleAsync(
            Guid productId,
            ICommandDispatcher commandDispatcher,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commandDispatcher);
        ArgumentNullException.ThrowIfNull(httpContext);

        httpContext.Response.Headers["Cache-Control"] =
            "private, no-store";

        var result = await commandDispatcher.DispatchAsync(
                new PublishProductCommand(productId),
                cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return CatalogApiProblemDetails.Create(
                result.Error!,
                httpContext);
        }

        return TypedResults.Ok(
            new PublishProductHttpResponse(
                result.Value.ProductId.Value,
                "published",
                result.Value.PublishedAtUtc));
    }
}
