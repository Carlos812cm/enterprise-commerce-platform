using System.Globalization;
using Catalog.Api.Authorization;
using Catalog.Api.Errors;
using Catalog.Application.Products.CreateDraftProduct;
using Commerce.Application.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace Catalog.Api.Endpoints.Products.CreateDraftProduct;

internal static class CreateDraftProductEndpoint
{
    public static RouteHandlerBuilder MapCreateDraftProduct(
        this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return group
            .MapPost(
                "/products",
                HandleAsync)
            .WithName("CreateDraftProduct")
            .WithSummary("Create a draft Catalog product")
            .WithDescription(
                "Creates an empty product draft. Options and variants are added through separate operations.")
            .WithTags("Catalog Products")
            .Accepts<CreateDraftProductRequest>(
                "application/json")
            .Produces<CreateDraftProductHttpResponse>(
                StatusCodes.Status201Created)
            .ProducesProblem(
                StatusCodes.Status400BadRequest)
            .ProducesProblem(
                StatusCodes.Status401Unauthorized)
            .ProducesProblem(
                StatusCodes.Status403Forbidden)
            .ProducesProblem(
                StatusCodes.Status409Conflict)
            .ProducesProblem(
                StatusCodes.Status500InternalServerError)
            .RequireAuthorization(
                CatalogAuthorization
                    .ManageProductsPolicy);
    }

    private static async Task<
        Results<
            Created<CreateDraftProductHttpResponse>,
            ProblemHttpResult>>
        HandleAsync(
            CreateDraftProductRequest request,
            ICommandDispatcher commandDispatcher,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(commandDispatcher);
        ArgumentNullException.ThrowIfNull(httpContext);

        var result =
            await commandDispatcher.DispatchAsync(
                    new CreateDraftProductCommand(
                        request.Name,
                        request.Slug,
                        request.Description),
                    cancellationToken)
                .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return CatalogApiProblemDetails.Create(
                result.Error!,
                httpContext);
        }

        var response =
            new CreateDraftProductHttpResponse(
                result.Value.ProductId.Value,
                "draft");

        var location = string.Concat(
            "/api/catalog/products/",
            response.ProductId.ToString(
                "D",
                CultureInfo.InvariantCulture));

        return TypedResults.Created(
            location,
            response);
    }
}
