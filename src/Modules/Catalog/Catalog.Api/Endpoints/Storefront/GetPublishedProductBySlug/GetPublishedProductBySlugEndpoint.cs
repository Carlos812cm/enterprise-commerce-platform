using System.Globalization;
using Catalog.Api.Errors;
using Catalog.Application.Products.GetPublishedProductBySlug;
using Commerce.Application.Messaging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace Catalog.Api.Endpoints.Storefront.GetPublishedProductBySlug;

internal static class GetPublishedProductBySlugEndpoint
{
    private const string CacheControlValue =
        "public, max-age=30, stale-while-revalidate=30";

    public static RouteHandlerBuilder
        MapGetPublishedProductBySlug(
            this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        return group
            .MapGet(
                "/products/{slug}",
                HandleAsync)
            .WithName(
                "GetPublishedProductBySlug")
            .WithSummary(
                "Get a published storefront product")
            .WithDescription(
                "Returns a public Catalog product and its active variants.")
            .WithTags(
                "Storefront Products")
            .Produces<
                PublishedProductHttpResponse>(
                StatusCodes.Status200OK)
            .Produces(
                StatusCodes.Status304NotModified)
            .ProducesProblem(
                StatusCodes.Status400BadRequest)
            .ProducesProblem(
                StatusCodes.Status404NotFound)
            .ProducesProblem(
                StatusCodes.Status500InternalServerError)
            .AllowAnonymous();
    }

    private static async Task<
        Results<
            Ok<PublishedProductHttpResponse>,
            StatusCodeHttpResult,
            ProblemHttpResult>>
        HandleAsync(
            string slug,
            IQueryDispatcher queryDispatcher,
            HttpContext httpContext,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            queryDispatcher);

        ArgumentNullException.ThrowIfNull(
            httpContext);

        var result =
            await queryDispatcher.DispatchAsync(
                    new GetPublishedProductBySlugQuery(
                        slug),
                    cancellationToken)
                .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return CatalogApiProblemDetails.Create(
                result.Error!,
                httpContext);
        }

        var product = result.Value;

        var entityTag =
            CreateEntityTag(
                product.ProductId,
                product.Version);

        ApplyCachingHeaders(
            httpContext,
            entityTag);

        if (MatchesIfNoneMatch(
                httpContext,
                entityTag))
        {
            return TypedResults.StatusCode(
                StatusCodes.Status304NotModified);
        }

        return TypedResults.Ok(
            PublishedProductHttpResponse.From(
                product));
    }

    private static string CreateEntityTag(
        Guid productId,
        long version)
    {
        return string.Concat(
            "W/\"",
            productId.ToString(
                "N",
                CultureInfo.InvariantCulture),
            "-",
            version.ToString(
                CultureInfo.InvariantCulture),
            "\"");
    }

    private static void ApplyCachingHeaders(
        HttpContext httpContext,
        string entityTag)
    {
        httpContext.Response.Headers[
            HeaderNames.ETag] =
            entityTag;

        httpContext.Response.Headers[
            HeaderNames.CacheControl] =
            CacheControlValue;

        httpContext.Response.Headers[
            HeaderNames.Vary] =
            "Accept-Encoding";
    }

    private static bool MatchesIfNoneMatch(
        HttpContext httpContext,
        string entityTag)
    {
        var values =
            httpContext.Request.Headers[
                HeaderNames.IfNoneMatch];

        foreach (var rawValue in values)
        {
            if (string.IsNullOrWhiteSpace(
                    rawValue))
            {
                continue;
            }

            var candidates =
                rawValue.Split(
                    ',',
                    StringSplitOptions
                        .RemoveEmptyEntries |
                    StringSplitOptions
                        .TrimEntries);

            foreach (var candidate in candidates)
            {
                if (string.Equals(
                        candidate,
                        "*",
                        StringComparison.Ordinal) ||
                    string.Equals(
                        candidate,
                        entityTag,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
